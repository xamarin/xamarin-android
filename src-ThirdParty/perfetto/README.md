# Introduction

This directory contains a copy of the [Perfetto](https://perfetto.dev/) SDK, which is
an amalgamate of the Perfetto header and source files meant to be used in client
applications.  We chose copying the sources over adding a Perfetto submodule in order
to save time and space.

The SDK should be updated manually, whenever it is deemed necessary.

# Updating

Whenever Perfetto is released, the SDK is generated in their release branch and placed
in the [sdk](https://github.com/google/perfetto/tree/v46.0/sdk) directory.  Every release
is accompanied by a tag whose name is the released Perfetto version (e.g. `v46.0`).  This
tag needs to be checked out on one's local machine, and the contents of the `sdk` directory
copied to the `src-ThirdParty/perfetto/` in our repository.

In order to learn what is the current version of our Perfetto copy, one needs to open
the `src-ThirdParty/perfetto/perfetto.cc` file and find the definition of the `PERFETTO_VERSION_STRING()`
macro:

```c++
#define PERFETTO_VERSION_STRING() "v46.0-7114ea53e"
```
