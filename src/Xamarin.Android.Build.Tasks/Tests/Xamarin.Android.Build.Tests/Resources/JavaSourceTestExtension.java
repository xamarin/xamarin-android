package com.xamarin.android.test.msbuildtest;
public class JavaSourceTestExtension extends JavaSourceJarTest {
	public String greetWithQuestion (String name, java.util.Date date, String question) {
		return greet (name, date) + question;
	}
}
