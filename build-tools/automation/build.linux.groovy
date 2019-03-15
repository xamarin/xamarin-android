// This file is based on the Jenkins scripted pipeline (as opposed to the declarative pipeline) syntax
// https://jenkins.io/doc/book/pipeline/syntax/#scripted-pipeline

def XADir = "xamarin-android"
def buildTarget = 'jenkins'
def chRootPackages = 'xvfb xauth mono-devel autoconf automake build-essential vim-common p7zip-full cmake gettext libtool libgdk-pixbuf2.0-dev intltool pkg-config ruby scons wget xz-utils git nuget ca-certificates-mono clang g++-mingw-w64 gcc-mingw-w64 libzip-dev openjdk-8-jdk unzip lib32stdc++6 lib32z1 libtinfo-dev:i386 linux-libc-dev:i386 zlib1g-dev:i386 gcc-multilib g++-multilib referenceassemblies-pcl zip fsharp psmisc libz-mingw-w64-dev msbuild mono-csharp-shell devscripts fakeroot debhelper libsqlite3-dev sqlite3 libc++-dev cli-common-dev mono-llvm-support curl'
def isPr = false                // Default to CI
def isStable = false            // Stable build workflow
def pBuilderBindMounts = null
def utils = null
def hasPrLabelFullMonoIntegrationBuild = false

def execChRootCommand(chRootName, chRootPackages, pBuilderBindMounts, makeCommand) {
    chroot chrootName: chRootName,
        additionalPackages: chRootPackages,
        bindMounts: pBuilderBindMounts,
        command: """
            export LC_ALL=en_US.UTF-8
            export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/games:/usr/local/games
            locale

            ${makeCommand}
            """
}

timestamps {
    node("${env.BotLabel}") {
        def scmVars = null

        stage ("checkout") {
            dir (XADir) {
                scmVars = checkout scm
            }
        }

        def workspace = "/mnt/jenkins/workspace/${env.JOB_BASE_NAME}"
        utils = load "${workspace}/${XADir}/build-tools/automation/utils.groovy"

        utils.stageWithTimeout('init', 30, 'MINUTES', XADir, true) {    // Typically takes less than a second for CI builds; execChRootCommand execution can take several minutes for PR builds
            // Note: PR plugin environment variable settings available here: https://wiki.jenkins.io/display/JENKINS/GitHub+pull+request+builder+plugin
            isPr = env.ghprbActualCommit != null
            isStable = env.IsStable == '1'
            def branch = isPr ? env.GIT_BRANCH : scmVars.GIT_BRANCH
            def commit = isPr ? env.ghprbActualCommit : scmVars.GIT_COMMIT

            def buildType = isPr ? 'PR' : 'CI'

            echo "HostName: ${env.NODE_NAME}"
            echo "Git repo: ${env.GitRepo}"     // Defined as an environment variable in the jenkins build definition
            echo "Job: ${env.JOB_BASE_NAME}"
            echo "Branch: ${branch}"
            echo "Commit: ${commit}"
            echo "Build type: ${buildType}"
            echo "Stable build workflow: ${isStable}"

            pBuilderBindMounts = "/home/${env.USER}"
            echo "pBuilderBindMounts: ${pBuilderBindMounts}"

            if (isPr) {
                echo "PR id: ${env.ghprbPullId}"
                echo "PR link: ${env.ghprbPullLink}"

                // Clear out the PR title and description. This is the equivalent of $JENKINS_HOME/global-pre-script/remove-problematic-ghprb-parameters.groovy used by freestyle builds
                echo "Clearing the PR title and description environment variables to avoid any special characters contained within from tripping up the build"
                env.ghprbPullTitle = ''
                env.ghprbPullLongDescription = ''

                chRootPackages = 'mono-devel autoconf automake build-essential vim-common p7zip-full cmake gettext libtool libgdk-pixbuf2.0-dev intltool pkg-config ruby scons wget xz-utils git nuget ca-certificates-mono g++-mingw-w64 gcc-mingw-w64 libzip4 openjdk-8-jdk unzip lib32stdc++6 lib32z1 libtinfo-dev:i386 linux-libc-dev:i386 zlib1g-dev:i386 gcc-multilib g++-multilib referenceassemblies-pcl zip libz-mingw-w64-dev msbuild mono-csharp-shell libsqlite3-dev sqlite3 cli-common-dev curl'

                if (utils.hasPrLabel(env.GitRepo, env.ghprbPullId, 'full-mono-integration-build')) {
                    hasPrLabelFullMonoIntegrationBuild = true
                    buildTarget = 'jenkins'
                } else {
                    buildTarget = 'all'
                }

                execChRootCommand(env.ChRootName, chRootPackages, pBuilderBindMounts,
                        """
                        if [ -z "\$JAVA_HOME" ]; then
                            if [ -f /etc/profile.d/jdk.sh ]; then
                                source /etc/profile.d/jdk.sh
                            fi
                        fi
                        """)
            }

            sh "env"
        }

        utils.stageWithTimeout('build', 6, 'HOURS', XADir, true) {    // Typically takes 4 hours
            def buildCmd = ''
            if (isPr) {
                buildCmd = "make prepare ${buildTarget} CONFIGURATION=${env.BuildFlavor} NO_SUDO=true V=1"
            } else {
                buildCmd = "make ${buildTarget} CONFIGURATION=${env.BuildFlavor} V=1 NO_SUDO=true MSBUILD_ARGS='/p:MonoRequiredMinimumVersion=5.12'"
            }

            execChRootCommand(env.ChRootName, chRootPackages, pBuilderBindMounts, buildCmd)
        }

        if (!isPr) {
            utils.stageWithTimeout('package deb', 30, 'MINUTES', XADir, true) {    // Typically takes less than 5 minutes
                execChRootCommand(env.ChRootName, chRootPackages, pBuilderBindMounts,
                                        "make package-deb CONFIGURATION=${env.BuildFlavor} V=1")
            }
        }

        if (!isPr && !isStable) {
            utils.stageWithTimeout('build tests', 30, 'MINUTES', XADir, true) {    // Typically takes less than 10 minutes
                // Occasionally `make run-all-tests` "hangs"; we believe this might be a mono/2018-06 bug.
                // We'll install mono/2018-02 on the build machines and try using that, which requires
                execChRootCommand(env.ChRootName, chRootPackages, pBuilderBindMounts,
                                    "xvfb-run -a -- make all-tests CONFIGURATION=${env.BuildFlavor} V=1")
            }
        }

        utils.stageWithTimeout('process build results', 10, 'MINUTES', XADir, true) {    // Typically takes less than a minute
            try {
                echo "processing build status"
                execChRootCommand(env.ChRootName, chRootPackages, pBuilderBindMounts,
                                    "make package-build-status CONFIGURATION=${env.BuildFlavor}")
            } catch (error) {
                echo "ERROR : NON-FATAL : processBuildStatus: Unexpected error: ${error}"
            }
        }

        if (!isPr) {
            utils.stageWithTimeout('publish packages to Azure', 30, 'MINUTES', '', true, 3) {    // Typically takes less than a minute, but provide ample time in situations where logs may be quite large
                def publishBuildFilePaths = "${XADir}/xamarin-android/*xamarin.android*.tar*";
                publishBuildFilePaths = "${publishBuildFilePaths},${XADir}/xamarin-android/bin/${env.BuildFlavor}/bundle-*.zip"
                publishBuildFilePaths = "${publishBuildFilePaths},${XADir}/*.dsc"
                publishBuildFilePaths = "${publishBuildFilePaths},${XADir}/*.deb"
                publishBuildFilePaths = "${publishBuildFilePaths},${XADir}/build-status*"
                publishBuildFilePaths = "${publishBuildFilePaths},${XADir}/xa-build-status*"

                echo "publishBuildFilePaths: ${publishBuildFilePaths}"
                def commandStatus = utils.publishPackages(env.StorageCredentialId, env.ContainerName, env.StorageVirtualPath, publishBuildFilePaths)
                if (commandStatus != 0) {
                    error "publish packages to Azure FAILED, status: ${commandStatus}"    // Ensure stage is labeled as 'failed' and red failure indicator is displayed in Jenkins pipeline steps view
                }
            }
        }
 
        if (!isPr && !isStable) {
            utils.stageWithTimeout('run all tests', 360, 'MINUTES', XADir, false) {
                echo "running tests"

                execChRootCommand(env.ChRootName, chRootPackages, pBuilderBindMounts,
                        """
                            xvfb-run -a -- make run-all-tests CONFIGURATION=${env.BuildFlavor} V=1 || (killall adb && false)
                            killall adb || true
                        """)
            }
        }

        if (!isPr && !isStable) {
            utils.stageWithTimeout('publish test error logs to Azure', 30, 'MINUTES', '', false, 3) {  // Typically takes less than a minute, but provide ample time in situations where logs may be quite large
                echo "packaging test error logs"

                // UNDONE: TEST: Copied from build.groovy. Does this work for Linux build?
                execChRootCommand(env.ChRootName, chRootPackages, pBuilderBindMounts,
                                    "make -C ${XADir} -k package-test-results CONFIGURATION=${env.BuildFlavor}")

                // UNDONE: TEST: Copied from build.groovy. Does this work for Linux build?
                def publishTestFilePaths = "${XADir}/xa-test-results*,${XADir}/test-errors.zip"

                echo "publishTestFilePaths: ${publishTestFilePaths}"
                def commandStatus = utils.publishPackages(env.StorageCredentialId, env.ContainerName, env.StorageVirtualPath, publishTestFilePaths)
                if (commandStatus != 0) {
                    error "publish test error logs to Azure FAILED, status: ${commandStatus}"    // Ensure stage is labeled as 'failed' and red failure indicator is displayed in Jenkins pipeline steps view
                }
            }
        }

        if (!isPr && !isStable) {
            utils.stageWithTimeout('Publish test results', 5, 'MINUTES', XADir, false, 3) {    // Typically takes under 1 minute to publish test results
                def initialStageResult = currentBuild.currentResult

                xunit thresholds: [
                        failed(unstableNewThreshold: '0', unstableThreshold: '0'),
                        skipped()                                                       // Note: Empty threshold settings per settings in the xamarin-android freestyle build are not permitted here
                    ],
                    tools: [
                        NUnit2(deleteOutputFiles: true,
                                failIfNotNew: true,
                                pattern: 'TestResult-*.xml',
                                skipNoTestFiles: true,
                                stopProcessingIfError: false)
                    ]

                if (initialStageResult == 'SUCCESS' && currentBuild.currentResult == 'UNSTABLE') {
                    error "One or more tests failed"                // Force an error condition if there was a test failure to indicate that this stage was the source of the build failure
                }
            }
        }
    }
}