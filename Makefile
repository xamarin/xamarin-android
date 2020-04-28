-include bin/configuration.mk

V             ?= 0
prefix         = /usr/local
CONFIGURATION ?= Debug
RUNTIME       := $(shell which mono64 2> /dev/null && echo mono64 || echo mono) --debug=casts
SOLUTION       = Xamarin.Android.sln
TEST_TARGETS   = build-tools/scripts/RunTests.targets
API_LEVEL     ?=
PREPARE_ARGS =
PREPARE_BUILD_LOG = bin/Build$(CONFIGURATION)/bootstrap-build.binlog
PREPARE_RESTORE_LOG = bin/Build$(CONFIGURATION)/bootstrap-restore.binlog
PREPARE_SOURCE_DIR = build-tools/xaprepare
PREPARE_SOLUTION = $(PREPARE_SOURCE_DIR)/xaprepare.sln
PREPARE_EXE = $(PREPARE_SOURCE_DIR)/xaprepare/bin/$(CONFIGURATION)/xaprepare.exe
PREPARE_COMMON_MSBUILD_FLAGS = /p:Configuration=$(CONFIGURATION) $(PREPARE_MSBUILD_ARGS) $(MSBUILD_ARGS)
PREPARE_MSBUILD_FLAGS = /binaryLogger:"$(PREPARE_BUILD_LOG)" $(PREPARE_COMMON_MSBUILD_FLAGS)
PREPARE_RESTORE_FLAGS = /binaryLogger:"$(PREPARE_RESTORE_LOG)" $(PREPARE_COMMON_MSBUILD_FLAGS)
PREPARE_SCENARIO =
PREPARE_CI_PR ?= 0
PREPARE_CI ?= 0
PREPARE_AUTOPROVISION ?= 0
PREPARE_IGNORE_MONO_VERSION ?= 1

_PREPARE_CI_MODE_PR_ARGS = --no-emoji --run-mode=CI
_PREPARE_CI_MODE_ARGS = $(_PREPARE_CI_MODE_PR_ARGS) -a
_PREPARE_ARGS =

BOOTSTRAP_SOLUTION = Xamarin.Android.BootstrapTasks.sln
BOOTSTRAP_BUILD_LOG = bin/Build$(CONFIGURATION)/bootstrap-build.binlog
BOOTSTRAP_MSBUILD_FLAGS = /t:Restore,Build /binaryLogger:"$(BOOTSTRAP_BUILD_LOG)" $(PREPARE_COMMON_MSBUILD_FLAGS)

all:
	$(call MSBUILD_BINLOG,all,$(_SLN_BUILD)) /restore $(MSBUILD_FLAGS) $(SOLUTION)

-include bin/Build$(CONFIGURATION)/rules.mk

ifeq ($(OS_NAME),)
export OS_NAME       := $(shell uname)
endif

ifeq ($(OS_ARCH),)
export OS_ARCH       := $(shell uname -m)
endif

export NO_SUDO       ?= false

ifneq ($(NO_SUDO),false)
_PREPARE_ARGS += --auto-provisioning-uses-sudo=false
endif

ifneq ($(V),0)
MONO_OPTIONS   += --debug
NUGET_VERBOSITY = -Verbosity Detailed
_PREPARE_ARGS += -v:d
endif

ifneq ($(PREPARE_CI_PR),0)
_PREPARE_ARGS += $(_PREPARE_CI_MODE_PR_ARGS)
endif

ifneq ($(PREPARE_CI),0)
_PREPARE_ARGS += $(_PREPARE_CI_MODE_ARGS)
endif

ifneq ($(PREPARE_AUTOPROVISION),0)
_PREPARE_ARGS += --auto-provision=yes --auto-provision-uses-sudo=yes
endif

ifeq ($(OS_NAME),Darwin)
ifeq ($(HOMEBREW_PREFIX),)
HOMEBREW_PREFIX ?= $(shell brew --prefix)
endif
else
HOMEBREW_PREFIX := $prefix
endif

ifeq ($(wildcard Configuration.OperatingSystem.props),)
PREPARE_MSBUILD_FLAGS += "/p:HostHomebrewPrefix=$(HOMEBREW_PREFIX)"
endif

ifneq ($(PREPARE_SCENARIO),)
_PREPARE_ARGS += -s:"$(PREPARE_SCENARIO)"
endif

ifeq ($(XA_FORCE_COMPONENT_REFRESH),true)
_PREPARE_ARGS += -refresh
endif

ifneq ($(PREPARE_IGNORE_MONO_VERSION),0)
_PREPARE_ARGS += --ignore-mono-version
endif

_PREPARE_ARGS += $(PREPARE_ARGS)

include build-tools/scripts/msbuild.mk

ifeq ($(USE_MSBUILD),1)
_SLN_BUILD  = $(MSBUILD)
else    # $(MSBUILD) != 1
_SLN_BUILD  = MSBUILD="$(MSBUILD)" tools/scripts/xabuild
endif   # $(USE_MSBUILD) == 1

ifneq ($(API_LEVEL),)
MSBUILD_FLAGS += /p:AndroidApiLevel=$(API_LEVEL) /p:AndroidFrameworkVersion=$(word $(API_LEVEL), $(ALL_FRAMEWORKS)) /p:AndroidPlatformId=$(word $(a), $(ALL_PLATFORM_IDS))
endif

all-tests::
	MSBUILD="$(MSBUILD)" $(call MSBUILD_BINLOG,all-tests,tools/scripts/xabuild) /restore $(MSBUILD_FLAGS) Xamarin.Android-Tests.sln

install::
	@if [ ! -d "bin/$(CONFIGURATION)" ]; then \
		echo "run 'make all' before you execute 'make install'!"; \
		exit 1; \
	fi
	-mkdir -p "$(prefix)/bin"
	-mkdir -p "$(prefix)/lib/mono/xbuild-frameworks"
	-mkdir -p "$(prefix)/lib/xamarin.android"
	-mkdir -p "$(prefix)/lib/mono/xbuild/Xamarin/"
	cp -a "bin/$(CONFIGURATION)/lib/xamarin.android/." "$(prefix)/lib/xamarin.android/"
	-rm -rf "$(prefix)/lib/mono/xbuild/Novell"
	-rm -rf "$(prefix)/lib/mono/xbuild/Xamarin/Xamarin.Android.Sdk.props"
	-rm -rf "$(prefix)/lib/mono/xbuild/Xamarin/Xamarin.Android.Sdk.targets"
	-rm -rf "$(prefix)/lib/mono/xbuild/Xamarin/Android"
	-rm -rf "$(prefix)/lib/mono/xbuild-frameworks/MonoAndroid"
	ln -s "$(prefix)/lib/xamarin.android/xbuild/Xamarin/Android/" "$(prefix)/lib/mono/xbuild/Xamarin/Android"
	ln -s "$(prefix)/lib/xamarin.android/xbuild/Novell/" "$(prefix)/lib/mono/xbuild/Novell"
	ln -s "$(prefix)/lib/xamarin.android/xbuild-frameworks/MonoAndroid/" "$(prefix)/lib/mono/xbuild-frameworks/MonoAndroid"
	if [ ! -e "$(prefix)/bin/mono" ]; then \
		cp tools/scripts/xabuild "$(prefix)/bin/xabuild"; \
	fi

uninstall::
	rm -rf "$(prefix)/lib/xamarin.android/" "$(prefix)/bin/xabuild"
	rm -rf "$(prefix)/lib/mono/xbuild/Xamarin/Android"
	rm -rf "$(prefix)/lib/mono/xbuild-frameworks/MonoAndroid"

topdir  := $(shell pwd)

# Used by External XA Build
EXTERNAL_XA_PATH=$(topdir)
EXTERNAL_GIT_PATH=$(topdir)/external

-include $(EXTERNAL_GIT_PATH)/monodroid/xa-integration.mk

include build-tools/scripts/BuildEverything.mk

# Must be after BuildEverything.mk - it uses variables defined there
include build-tools/scripts/Packaging.mk

run-all-tests:
	@echo "PRINTING MONO VERSION"
	mono --version
	_r=0 ; \
	$(call MSBUILD_BINLOG,run-all-tests,,Test) $(TEST_TARGETS) /t:RunAllTests || _r=$$? ; \
	exit $$_r

clean:
	$(call MSBUILD_BINLOG,clean) /t:Clean Xamarin.Android.sln
	tools/scripts/xabuild $(MSBUILD_FLAGS) /t:Clean Xamarin.Android-Tests.sln

distclean:
	# It may fail if we're cleaning a half-built tree, no harm done if we ignore it
	-$(MAKE) clean
	git clean -xdff
	git submodule foreach git clean -xdff

run-nunit-tests:
ifeq ($(SKIP_NUNIT_TESTS),)
	$(call MSBUILD_BINLOG,run-nunit-tests,,Test) $(TEST_TARGETS) /t:RunNUnitTests
endif # $(SKIP_NUNIT_TESTS) == ''

run-ji-tests:
	$(call MSBUILD_BINLOG,run-ji-tests,,Test) $(TEST_TARGETS) /t:RunJavaInteropTests

ifneq ($(PACKAGES),)
APK_TESTS_PROP = /p:ApkTests='"$(PACKAGES)"'
endif

run-apk-tests:
	_r=0 ; \
	$(call MSBUILD_BINLOG,run-apk-tests,,Test) $(TEST_TARGETS) /t:RunApkTests /p:RunApkTestsTarget=RunPerformanceApkTests $(APK_TESTS_PROP) || _r=1 ; \
	$(call MSBUILD_BINLOG,run-apk-tests,,Test) $(TEST_TARGETS) /t:RunApkTests $(APK_TESTS_PROP) || _r=1 ; \
	exit $$_r

run-performance-tests:
	_r=0 ; \
	$(call MSBUILD_BINLOG,run-apk-tests,,Test) $(TEST_TARGETS) /t:RunApkTests /p:RunApkTestsTarget=RunPerformanceApkTests $(APK_TESTS_PROP) || _r=1 ; \
	$(call MSBUILD_BINLOG,run-performance-tests,,Test) $(TEST_TARGETS) /t:RunPerformanceTests || _r=1 ; \
	exit $$_r

list-nunit-tests:
	$(MSBUILD) $(MSBUILD_FLAGS) $(TEST_TARGETS) /t:ListNUnitTests

include build-tools/scripts/runtime-helpers.mk

.PHONY: prepare-build-init
prepare-build-init:
	mkdir -p $(dir $(PREPARE_BUILD_LOG))
	msbuild $(PREPARE_RESTORE_FLAGS) $(PREPARE_SOLUTION) /t:Restore

.PHONY: prepare-build
prepare-build: prepare-build-init
	msbuild $(PREPARE_MSBUILD_FLAGS) $(PREPARE_SOLUTION)

.PHONY: prepare
prepare: prepare-build
	mono --debug $(PREPARE_EXE) $(_PREPARE_ARGS)
	msbuild $(BOOTSTRAP_MSBUILD_FLAGS) $(BOOTSTRAP_SOLUTION)

.PHONY: prepare-help
prepare-help: prepare-build
	mono --debug $(PREPARE_EXE) -h

.PHONY: shutdown-compiler-server
shutdown-compiler-server:
	# Ensure the VBCSCompiler.exe process isn't running during the mono update
	pgrep -lfi VBCSCompiler.exe 2>/dev/null || true
	@pid=`pgrep -lfi VBCSCompiler.exe 2>/dev/null | awk '{ print $$1 }'` ; \
	echo "VBCSCompiler process ID (if running): $$pid" ;\
	if [[ -n "$$pid" ]]; then \
		echo "Terminating the VBCSCompiler '$$pid' server process prior to updating mono" ; \
		exitCode=0 ;\
		kill -HUP $$pid 2>/dev/null || exitCode=$$? ;\
		if [[ $$exitCode -eq 0 ]]; then \
			sleep 2 ;\
			pgrep -lfi VBCSCompiler.exe 2>/dev/null&&echo "ERROR: VBCSCompiler server still exists" || echo "Verified that the VBCSCompiler server process no longer exists" ;\
		else \
			echo "ERROR: Kill command failed with exit code $$exitCode" ;\
		fi \
	fi

.PHONY: prepare-update-mono
prepare-update-mono: prepare-build shutdown-compiler-server
	mono --debug $(PREPARE_EXE) $(_PREPARE_ARGS) -s:UpdateMono

prepare-external-git-dependencies: prepare-build
	mono --debug $(PREPARE_EXE) $(_PREPARE_ARGS) -s:PrepareExternalGitDependencies

APK_SIZES_REFERENCE_DIR=tests/apk-sizes-reference

update-apk-sizes-reference:
	-mkdir -p $(APK_SIZES_REFERENCE_DIR)
	cp -v *values-$(CONFIGURATION).csv $(APK_SIZES_REFERENCE_DIR)/
