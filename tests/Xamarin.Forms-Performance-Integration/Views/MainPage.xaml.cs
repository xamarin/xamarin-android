﻿using System;

using Xamarin.Forms;

namespace Xamarin.Forms.Performance.Integration
{
	public partial class MainPage : TabbedPage
	{
		public MainPage ()
		{
			InitializeComponent ();

			Page itemsPage, aboutPage = null;

			switch (Device.RuntimePlatform) {
				case Device.iOS:
					itemsPage = new NavigationPage (new ItemsPage ()) {
						Title = "Browse"
					};

					aboutPage = new NavigationPage (new AboutPage ()) {
						Title = "About"
					};
					break;
				default:
					itemsPage = new ItemsPage () {
						Title = "Browse"
					};

					aboutPage = new AboutPage () {
						Title = "About"
					};
					break;
			}

			Children.Add (itemsPage);
			Children.Add (aboutPage);

			Title = Children [0].Title;
		}

		protected override void OnCurrentPageChanged ()
		{
			base.OnCurrentPageChanged ();
			Title = CurrentPage?.Title ?? string.Empty;
		}
	}
}