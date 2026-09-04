using System;
using System.Text;

namespace KeyboardDebounce
{
    internal sealed class SaveErrorStatus
    {
        private string _learningError;
        private string _settingsError;

        public bool SetError(bool settings, string message)
        {
            if (settings)
            {
                if (String.Equals(_settingsError, message, StringComparison.Ordinal))
                {
                    return false;
                }
                _settingsError = message;
            }
            else
            {
                if (String.Equals(_learningError, message, StringComparison.Ordinal))
                {
                    return false;
                }
                _learningError = message;
            }
            return true;
        }

        public bool ClearError(bool settings)
        {
            if (settings)
            {
                if (_settingsError == null) return false;
                _settingsError = null;
            }
            else
            {
                if (_learningError == null) return false;
                _learningError = null;
            }
            return true;
        }

        public string GetVisibleStatus(string fallbackStatus)
        {
            if (String.IsNullOrEmpty(_settingsError)
                && String.IsNullOrEmpty(_learningError))
            {
                return fallbackStatus;
            }

            var status = new StringBuilder("保存失败，正在自动重试：");
            if (!String.IsNullOrEmpty(_settingsError))
            {
                status.Append(" ").Append(_settingsError);
            }
            if (!String.IsNullOrEmpty(_learningError))
            {
                status.Append(" ").Append(_learningError);
            }
            return status.ToString();
        }
    }
}
