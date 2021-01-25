using System;

namespace Xamarin.Android.Prepare
{
	abstract class Characters
	{
		public abstract string   Bullet { get; }
		public abstract string   Link { get; }
		public abstract string   Package { get; }
		public abstract string   LeftArrow { get; }
		public abstract string   RightArrow { get; }
		public abstract string   Success { get; }
		public abstract string   Failure { get; }
		public abstract string[] Twiddler { get; }

		protected Characters ()
		{}

		public static Characters Create (Context context)
		{
			if (context == null)
				throw new ArgumentNullException (nameof (context));

			if (!context.NoEmoji && !context.DullMode && context.CanConsoleUseUnicode)
				return new UnicodeChars ();

			return new PlainChars ();
		}
	}

	sealed class PlainChars : Characters
	{
		static readonly string[] twiddler = new [] {"-", "\\", "|", "/", "-", "\\", "|", "/"};

		public override string   Bullet => "*";
		public override string   Link => "->";
		public override string   Package => "#";
		public override string   LeftArrow => "<-";
		public override string   RightArrow => "->";
		public override string   Success => "+";
		public override string   Failure => "-";
		public override string[] Twiddler => twiddler;
	}

	sealed class UnicodeChars : Characters
	{
		readonly string[] twiddler;

		public override string   Bullet => "•";
		public override string   Link => Char.ConvertFromUtf32 (0x1f517); // 🔗
		public override string   Package => Char.ConvertFromUtf32 (0x1f4e6); // 📦
		public override string   LeftArrow => "←";
		public override string   RightArrow => "→";
		public override string   Success => "✔";
		public override string   Failure => "✘";
		public override string[] Twiddler => twiddler;

		public UnicodeChars ()
		{
			twiddler = new string [12];

			for (int i = 0 ; i < 12; i++) {
				twiddler [i] = Char.ConvertFromUtf32 (0x1F550 + i); // 🕐🕑🕒🕓🕔🕕🕖🕗🕘🕙🕚🕛
			}
		}
	}
}
