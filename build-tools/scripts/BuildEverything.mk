PRODUCT_VERSION   = $(shell $(MSBUILD) $(MSBUILD_FLAGS) /p:DoNotLoadOSProperties=True /nologo /v:minimal /t:GetProductVersion build-tools/scripts/Info.targets | tr -d '[[:space:]]')

GIT_BRANCH        = $(shell LANG=C git rev-parse --abbrev-ref HEAD | tr -d '[[:space:]]' | tr -C a-zA-Z0-9- _)
GIT_COMMIT        = $(shell LANG=C git log --no-color --first-parent -n1 --pretty=format:%h)

# In which commit did $(PRODUCT_VERSION) change? 00000000 if uncommitted
-commit-of-last-version-change    = $(shell LANG=C git blame Configuration.props | grep '<ProductVersion>' | grep -v grep | sed 's/ .*//')

# How many commits have passed since $(-commit-of-last-version-change)?
# "0" when commit hash is invalid (e.g. 00000000)
-num-commits-since-version-change = $(shell LANG=C git log $(-commit-of-last-version-change)..HEAD --oneline 2>/dev/null | wc -l | sed 's/ //g')

ifeq ($(OS_NAME),Linux)
ZIP_EXTENSION         = tar.bz2
else
ZIP_EXTENSION         = zip
endif

ZIP_OUTPUT_BASENAME   = xamarin.android-oss_v$(PRODUCT_VERSION).$(-num-commits-since-version-change)_$(OS_NAME)-$(OS_ARCH)_$(GIT_BRANCH)_$(GIT_COMMIT)
ZIP_OUTPUT            = $(ZIP_OUTPUT_BASENAME).$(ZIP_EXTENSION)


## The following values *must* use SPACE, **not** TAB, to separate values.

# $(ALL_API_LEVELS) and $(ALL_FRAMEWORKS) must be kept in sync w/ each other
ALL_API_LEVELS    = 1 2 3 4 5 6 7 8 9 10    11  12  13  14  15      16    17    18    19    20        21    22    23    24    25    26    27    28
# this was different from ALL_API_LEVELS when API Level 26 was "O". Same could happen in the future.
ALL_PLATFORM_IDS  = 1 2 3 4 5 6 7 8 9 10    11  12  13  14  15      16    17    18    19    20        21    22    23    24    25    26    27    P
# supported api levels
ALL_FRAMEWORKS    = _ _ _ _ _ _ _ _ _ v2.3  _   _   _   _   v4.0.3  v4.1  v4.2  v4.3  v4.4  v4.4.87   v5.0  v5.1  v6.0  v7.0  v7.1  v8.0  v8.1  v8.1.99
API_LEVELS        =                   10                    15      16    17    18    19    20        21    22    23    24    25    26    27    28
STABLE_API_LEVELS =                   10                    15      16    17    18    19    20        21    22    23    24    25    26    27

## The preceding values *must* use SPACE, **not** TAB, to separate values.


FRAMEWORKS        = $(foreach a, $(API_LEVELS), $(word $(a),$(ALL_FRAMEWORKS)))
STABLE_FRAMEWORKS = $(foreach a, $(STABLE_API_LEVELS), $(word $(a),$(ALL_FRAMEWORKS)))
PLATFORM_IDS      = $(foreach a, $(API_LEVELS), $(word $(a),$(ALL_PLATFORM_IDS)))

ALL_JIT_ABIS  = \
	armeabi \
	armeabi-v7a \
	arm64-v8a \
	x86 \
	x86_64

ALL_HOST_ABIS = \
	$(shell uname)

ALL_AOT_ABIS = \
	armeabi \
	arm64 \
	x86 \
	x86_64 \

ifneq ($(OS_NAME),Linux)
ALL_HOST_ABIS += \
	mxe-Win32 \
	mxe-Win64


ALL_AOT_ABIS += \
	win-armeabi \
	win-arm64 \
	win-x86 \
	win-x86_64
endif

ifneq ($(OS_NAME),Linux)
MONO_OPTIONS += --arch=64
endif

_space :=
_space +=

# usage: $(call join-with,SEPARATOR,LIST)
# Joins elements of LISt with SEPARATOR.
join-with = $(subst $(_space),$(1),$(strip $(2)))


_MSBUILD_ARGS	= \
	/p:AndroidSupportedTargetJitAbis=$(call join-with,:,$(ALL_JIT_ABIS)) \
	/p:AndroidSupportedHostJitAbis=$(call join-with,:,$(ALL_HOST_ABIS)) \
	/p:AndroidSupportedTargetAotAbis=$(call join-with,:,$(ALL_AOT_ABIS))

CONFIGURATIONS ?= Debug Release

.PHONY: leeroy jenkins leeroy-all opentk-jcw framework-assemblies
.PHONY: create-vsix

jenkins: prepare leeroy $(ZIP_OUTPUT)

leeroy: leeroy-all framework-assemblies opentk-jcw

leeroy-all:
	$(foreach conf, $(CONFIGURATIONS), \
		$(_SLN_BUILD) $(MSBUILD_FLAGS) $(SOLUTION) /p:Configuration=$(conf) $(_MSBUILD_ARGS) && \
		$(call CREATE_THIRD_PARTY_NOTICES,$(conf),bin/$(conf)/lib/xamarin.android/ThirdPartyNotices.txt,$(THIRD_PARTY_NOTICE_LICENSE_TYPE),True,False) && ) \
	true

framework-assemblies:
	PREV_VERSION="v1.0"; \
	$(foreach a, $(API_LEVELS), \
		CUR_VERSION=`echo "$(ALL_FRAMEWORKS)"|tr -s " "|cut -d " " -s -f $(a)`; \
		$(foreach conf, $(CONFIGURATIONS), \
			REDIST_FILE=bin/$(conf)/lib/xamarin.android/xbuild-frameworks/MonoAndroid/$${CUR_VERSION}/RedistList/FrameworkList.xml; \
			grep -q $${PREV_VERSION} $${REDIST_FILE}; \
			if [ $$? -ne 0 ] ; then \
				rm -f bin/$(conf)/lib/xamarin.android/xbuild-frameworks/MonoAndroid/$${CUR_VERSION}/RedistList/FrameworkList.xml; \
			fi; \
			$(_SLN_BUILD) $(MSBUILD_FLAGS) src/Mono.Android/Mono.Android.csproj /p:Configuration=$(conf) $(_MSBUILD_ARGS) \
				/p:AndroidApiLevel=$(a) /p:AndroidPlatformId=$(word $(a), $(ALL_PLATFORM_IDS)) /p:AndroidFrameworkVersion=$${CUR_VERSION} \
				/p:AndroidPreviousFrameworkVersion=$${PREV_VERSION} || exit 1; ) \
		PREV_VERSION=$${CUR_VERSION}; )
	$(foreach conf, $(CONFIGURATIONS), \
		rm -f bin/$(conf)/lib/xamarin.android/xbuild-frameworks/MonoAndroid/v1.0/Xamarin.Android.NUnitLite.dll; \
		$(_SLN_BUILD) $(MSBUILD_FLAGS) src/Xamarin.Android.NUnitLite/Xamarin.Android.NUnitLite.csproj /p:Configuration=$(conf) $(_MSBUILD_ARGS) \
			/p:AndroidApiLevel=$(firstword $(API_LEVELS)) /p:AndroidPlatformId=$(word $(firstword $(API_LEVELS)), $(ALL_PLATFORM_IDS)) \
			/p:AndroidFrameworkVersion=$(firstword $(FRAMEWORKS)) || exit 1; )
	_latest_stable_framework=$$($(MSBUILD) /p:DoNotLoadOSProperties=True /nologo /v:minimal /t:GetAndroidLatestStableFrameworkVersion build-tools/scripts/Info.targets | tr -d '[[:space:]]') ; \
	$(foreach conf, $(CONFIGURATIONS), \
		rm -f "bin/$(conf)/lib/xamarin.android/xbuild-frameworks/MonoAndroid/$$_latest_stable_framework"/Mono.Android.Export.* ; \
		$(_SLN_BUILD) $(MSBUILD_FLAGS) src/Mono.Android.Export/Mono.Android.Export.csproj /p:Configuration=$(conf) $(_MSBUILD_ARGS) \
			/p:AndroidApiLevel=$(firstword $(API_LEVELS)) /p:AndroidPlatformId=$(word $(firstword $(API_LEVELS)), $(ALL_PLATFORM_IDS)) \
			/p:AndroidFrameworkVersion=$(firstword $(FRAMEWORKS)) || exit 1; ) \
	$(foreach conf, $(CONFIGURATIONS), \
		rm -f "bin/$(conf)/lib/xamarin.android/xbuild-frameworks/MonoAndroid/$$_latest_stable_framework"/OpenTK-1.0.* ; \
		$(_SLN_BUILD) $(MSBUILD_FLAGS) src/OpenTK-1.0/OpenTK.csproj /p:Configuration=$(conf) $(_MSBUILD_ARGS) \
			/p:AndroidApiLevel=$(firstword $(API_LEVELS)) /p:AndroidPlatformId=$(word $(firstword $(API_LEVELS)), $(ALL_PLATFORM_IDS)) \
			/p:AndroidFrameworkVersion=$(firstword $(FRAMEWORKS)) || exit 1; )

opentk-jcw:
	$(foreach a, $(API_LEVELS), \
		$(foreach conf, $(CONFIGURATIONS), \
			touch bin/$(conf)/lib/xamarin.android/xbuild-frameworks/MonoAndroid/*/OpenTK-1.0.dll; \
			$(_SLN_BUILD) $(MSBUILD_FLAGS) src/OpenTK-1.0/OpenTK.csproj /t:GenerateJavaCallableWrappers /p:Configuration=$(conf) $(_MSBUILD_ARGS) \
				/p:AndroidApiLevel=$(a) /p:AndroidPlatformId=$(word $(a), $(ALL_PLATFORM_IDS)) /p:AndroidFrameworkVersion=$(word $(a), $(ALL_FRAMEWORKS)) || exit 1; ))

_BUNDLE_ZIPS_INCLUDE  = \
	$(ZIP_OUTPUT_BASENAME)/ThirdPartyNotices.txt \
	$(ZIP_OUTPUT_BASENAME)/bin/Debug \
	$(ZIP_OUTPUT_BASENAME)/bin/Release

_BUNDLE_ZIPS_EXCLUDE  = \
	$(ZIP_OUTPUT_BASENAME)/bin/*/bundle-*.zip

# Usage: $(call CREATE_VSIX,configuration,unstable)
define CREATE_VSIX
		MONO_IOMAP=all MONO_OPTIONS="$(MONO_OPTIONS)" msbuild $(MSBUILD_FLAGS) /p:Configuration=$(1) /p:CreateVsixContainer=True \
			build-tools/create-vsix/create-vsix.csproj \
			$(if $(VSIX),"/p:VsixPath=$(VSIX)") \
			"/p:UnstableFrameworks=$(2)" \
			$(if $(UNSTABLE_VSIX),"/p:UnstableVsixPath=$(UNSTABLE_VSIX)") \
			$(if $(EXPERIMENTAL),/p:IsExperimental="$(EXPERIMENTAL)") \
			$(if $(PRODUCT_COMPONENT),/p:IsProductComponent="$(PRODUCT_COMPONENT)") \
			$(if $(PACKAGE_VERSION),/p:ProductVersion="$(PACKAGE_VERSION)") \
			$(if $(REPO_NAME),/p:XARepositoryName="$(REPO_NAME)") \
			$(if $(PACKAGE_HEAD_BRANCH),/p:XAVersionBranch="$(PACKAGE_HEAD_BRANCH)") \
			$(if $(PACKAGE_VERSION_REV),/p:XAVersionCommitCount="$(PACKAGE_VERSION_REV)") \
			$(if $(COMMIT),/p:XAVersionHash="$(COMMIT)")
endef # CREATE_VSIX

create-vsix:
	HAVE_UNSTABLE=$(shell $(MSBUILD) $(MSBUILD_FLAGS) /p:DoNotLoadOSProperties=True /nologo /v:minimal /t:HaveUnstableFrameworks build-tools/scripts/Info.targets | tr -d '[[:space:]]') ; \
	$(foreach conf, $(CONFIGURATIONS), \
		$(call CREATE_VSIX,$(conf),False) && \
		(if [ "$$HAVE_UNSTABLE" = "True" ] ; then $(call CREATE_VSIX,$(conf),True); fi;) && ) \
	true

package-oss-name:
	@echo ZIP_OUTPUT=$(ZIP_OUTPUT)

package-oss $(ZIP_OUTPUT):
	if [ -d bin/Debug/bin ]   ; then cp tools/scripts/xabuild bin/Debug/bin   ; fi
	if [ -d bin/Release/bin ] ; then cp tools/scripts/xabuild bin/Release/bin ; fi
	if [ ! -d $(ZIP_OUTPUT_BASENAME) ] ; then mkdir $(ZIP_OUTPUT_BASENAME) ; fi
	if [ ! -L $(ZIP_OUTPUT_BASENAME)/bin ] ; then ln -s ../bin $(ZIP_OUTPUT_BASENAME) ; fi
	if [ ! -f $(ZIP_OUTPUT_BASENAME)/ThirdPartyNotices.txt ] ; then \
		cp -n $(firstword $(wildcard bin/*/lib/xamarin.android/ThirdPartyNotices.txt)) $(ZIP_OUTPUT_BASENAME) ; \
	fi
	_exclude_list=".__exclude_list.txt"; \
	ls -1d $(_BUNDLE_ZIPS_EXCLUDE) > "$$_exclude_list" 2>/dev/null ; \
	for c in $(CONFIGURATIONS) ; do \
		_sl="$(ZIP_OUTPUT_BASENAME)/bin/$$c/lib/xamarin.android/xbuild/.__sys_links.txt"; \
		if [ ! -f "$$_sl" ]; then continue; fi; \
		for f in `cat $$_sl` ; do \
			echo "$(ZIP_OUTPUT_BASENAME)/bin/$$c/lib/xamarin.android/xbuild/$$f" >> "$$_exclude_list"; \
		done; \
	done
ifeq ($(ZIP_EXTENSION),zip)
	zip -r "$(ZIP_OUTPUT)" \
		`ls -1d $(_BUNDLE_ZIPS_INCLUDE) 2>/dev/null` \
		"-x@.__exclude_list.txt"
else ifeq ($(ZIP_EXTENSION),tar.bz2)
	tar --exclude-from=.__exclude_list.txt -cjhvf "$(ZIP_OUTPUT)" `ls -1d $(_BUNDLE_ZIPS_INCLUDE) 2>/dev/null`
endif
	-rm ".__exclude_list.txt"

package-deb: $(ZIP_OUTPUT)
	rm -fr $(ZIP_OUTPUT_BASENAME)
	tar xf $(ZIP_OUTPUT)
	cp -a build-tools/debian-metadata $(ZIP_OUTPUT_BASENAME)/debian
	sed "s/%CONFIG%/$(CONFIGURATION)/" $(ZIP_OUTPUT_BASENAME)/debian/xamarin.android-oss.install.in > $(ZIP_OUTPUT_BASENAME)/debian/xamarin.android-oss.install && rm -f $(ZIP_OUTPUT_BASENAME)/debian/xamarin.android-oss.install.in
	cp LICENSE $(ZIP_OUTPUT_BASENAME)/debian/copyright
	ln -sf $(ZIP_OUTPUT) xamarin.android-oss_$(PRODUCT_VERSION).$(-num-commits-since-version-change).orig.tar.bz2
	cd $(ZIP_OUTPUT_BASENAME) && DEBEMAIL="Xamarin Public Jenkins (auto-signing) <releng@xamarin.com>" dch --create -v $(PRODUCT_VERSION).$(-num-commits-since-version-change) --package xamarin.android-oss --force-distribution --distribution alpha "New release - please see git log for $(GIT_COMMIT)"
	cd $(ZIP_OUTPUT_BASENAME) && dpkg-buildpackage -us -uc -rfakeroot

package-test-errors:
ifneq ($(wildcard bin/Test*/temp),)
	zip -r test-errors.zip bin/Test*/temp
endif # We have test error output
