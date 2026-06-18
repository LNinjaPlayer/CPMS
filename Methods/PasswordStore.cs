using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;

namespace CPMS.Methods
{
	internal static class PasswordStore
	{
		private static readonly System.Threading.Lock _lock = new();
		private static string? _value;
		private static System.Timers.Timer? _timer;
		private readonly static int _timeout = Settings.TimeoutMs;
		public static string? Current
		{
			get
			{
				_lock.Enter();
				try { return _value; }
				finally { _lock.Exit(); }
			}
		}
		public static void Set(string? pw)
		{
			_lock.Enter();
			try
			{
				_value = pw;
				if (_timer != null)
				{
					_timer.Stop();
					_timer.Elapsed -= TimerElapsed;
					_timer.Dispose();
					_timer = null;
				}
				if (string.IsNullOrEmpty(pw) || _timeout <= 0) { return; }
				_timer = new System.Timers.Timer(_timeout) { AutoReset = false, };
				_timer.Elapsed += TimerElapsed;
				_timer.Start();
			}
			finally { _lock.Exit(); }
		}

		private static void TimerElapsed(object? s, ElapsedEventArgs e)
		{
			_lock.Enter();
			try
			{
				_value = null;
				if (_timer != null)
				{
					_timer.Elapsed -= TimerElapsed;
					_timer.Dispose();
					_timer = null;
				}
			}
			finally { _lock.Exit(); }
		}
		public static void Clear() { Set(null); }
	}
}
