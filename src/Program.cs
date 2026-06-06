using System;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Application.Run(new TrayAppContext());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Keyboard Debounce 启动失败：" + Environment.NewLine + ex.Message,
                    "Keyboard Debounce",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
