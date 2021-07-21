package testing;

public final class Run {
    private Run() {
    }

    public static interface CatchThrowableHandler {
        void onCatch(Throwable t);
    }

    public static final void tryCatchFinally (Runnable r, CatchThrowableHandler c, Runnable f) {
        try {
            r.run();
        }
        catch (Throwable t) {
            c.onCatch(t);
        }
        finally {
            f.run();
        }
    }
}
