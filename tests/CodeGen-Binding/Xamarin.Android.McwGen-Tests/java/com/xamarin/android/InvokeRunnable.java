package com.xamarin.android;

public interface InvokeRunnable<T extends Runnable> {
    void invoke (T runnable);
}
