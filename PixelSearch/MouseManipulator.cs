using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PixelSearch
{
	internal class MouseManipulator
	{
		[DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
		public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool SetCursorPos(int x, int y);

		//Mouse actions
		private const int MOUSEEVENTF_LEFTDOWN = 0x02;

		private const int MOUSEEVENTF_LEFTUP = 0x04;
		private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
		private const int MOUSEEVENTF_RIGHTUP = 0x10;


		public static void MouseClickLeft(int clickCount = 0)
		{
			if (clickCount == 1 || clickCount == 0)
			{
				var X = (uint) Cursor.Position.X;
				var Y = (uint) Cursor.Position.Y;
				mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, X, Y, 0, 0);
			}
			else
			{
				for (var i = 0; i < clickCount; i++)
				{
					var X = (uint) Cursor.Position.X;
					var Y = (uint) Cursor.Position.Y;
					mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, X, Y, 0, 0);
				}
			}
		}

		public static void MouseClickRight(int clickCount = 0)
		{
			if (clickCount == 1 || clickCount == 0)
			{
				var X = (uint) Cursor.Position.X;
				var Y = (uint) Cursor.Position.Y;
				mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, X, Y, 0, 0);
			}
			else
			{
				for (var i = 0; i < clickCount; i++)
				{
					var X = (uint) Cursor.Position.X;
					var Y = (uint) Cursor.Position.Y;
					mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, X, Y, 0, 0);
				}
			}
		}

		public static void MouseMove(int x, int y)
		{
			SetCursorPos(x, y);
		}
	}
}