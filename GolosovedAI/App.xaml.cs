using System.Text;
using System.Windows;
using System.Threading;
using System.Diagnostics;
using System;
using System.Runtime.InteropServices;

namespace GolosovedAI
{
    public partial class App : Application
    {
        private Mutex? _instanceMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (!TryAcquireMutex(out _instanceMutex))
            {
                ActivateExistingInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        private static bool TryAcquireMutex(out Mutex? mutex)
        {
            mutex = null;
            const string mutexName = "GolosovedAI_SingleInstance_Mutex_v1";

            try
            {
                bool createdNew;
                mutex = new Mutex(true, mutexName, out createdNew);
                return createdNew;
            }
            catch
            {
                // Если не удалось создать мьютекс, считаем, что экземпляр уже существует
                return false;
            }
        }

        private static void ActivateExistingInstance()
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var others = Process.GetProcessesByName(current.ProcessName)
                    .Where(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    .ToArray();

                if (others.Length == 0)
                    return; // не нашли окно

                var existing = others[0];
                IntPtr handle = existing.MainWindowHandle;

                if (handle != IntPtr.Zero)
                {
                    if (IsIconic(handle))
                        ShowWindowAsync(handle, SW_RESTORE);

                    SetForegroundWindow(handle);
                }
            }
            catch
            {
                // Игнорируем ошибки активации
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _instanceMutex?.ReleaseMutex();
                _instanceMutex?.Dispose();
            }
            catch
            {
                // Игнорируем ошибки освобождения
            }
            finally
            {
                _instanceMutex = null;
            }

            base.OnExit(e);
        }
    }
}