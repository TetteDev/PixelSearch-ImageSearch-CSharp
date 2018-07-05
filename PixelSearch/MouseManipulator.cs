using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PixelSearch
{
	
	class MouseManipulator
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


		public static void MouseClickLeft()
		{
			uint X = (uint)Cursor.Position.X;
			uint Y = (uint)Cursor.Position.Y;
			mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, X, Y, 0, 0);
		}

		public static void MouseClickRight()
		{
			uint X = (uint)Cursor.Position.X;
			uint Y = (uint)Cursor.Position.Y;
			mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, X, Y, 0, 0);
		}

		public static void MouseMove(int x, int y)
		{
			SetCursorPos(x, y);
		}
	}
}
