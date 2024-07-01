# Problems to solve

  * `libSystem.Security.Cryptography.Native.Android.a` contains the `JNI_OnLoad` function
    which initializes the whole crypto support library, but we can't use it as it would
    conflict with our own.  Potential solution is to modify the above library's source code
    to add an init function that we will call from our own `JNI_OnLoad` and make the library's
    init function do the same. The `JNI_OnLoad` object file would have to be omitted from the
    library's `.a`
  * `p/invoke usage`.
    Currently, all the BCL archives (with exception of the above crypto one) are
    linked into the unified runtime using `--whole-archive` - that is, they become part of the
    runtime in their entirety.  This is wasteful, but necessary, so that `p/invokes` into those
    libraries work correctly.  Instead, we should scan the application DLLs for p/invokes from
    those libraries and generate code to reference the required functions, so that the linker
    can do its job and remove code not used by the application.  Likely needed is a linker step.
  * `p/invoke` handling mechanism.  Right now, we `dlopen` the relevant `.so` library and look
    up the required symbol in there.  With the unified runtime the `.so` disappears, so we either
    need to look it up in our own library or, better, call the function directly.  The latter is
    a bit more complicated to implement but would give us much faster code, thus it's the preferred
    solution.

# Ideas

  * Use [mold](https://github.com/rui314/mold) which has recently been re-licensed under `MIT/X11`
    (and contains components licensed under a mixture of `BSD*` and `Apache 2.0` licenses), so we
    can easily redistribute it instead of the LLVM's `lld`.  The advantage is `mold`'s [speed](https://github.com/rui314/mold?tab=readme-ov-file#mold-a-modern-linker)
