using System;

namespace TatehamaATS_v1.Exceptions
{
    /// <summary>
    /// TatehamaATS例外クラス
    /// </summary>
    public class TatehamaATSException : Exception
    {
        public TatehamaATSException() : base() { }
        public TatehamaATSException(string message) : base(message) { }
        public TatehamaATSException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// ATS共通例外クラス
    /// </summary>
    public class ATSCommonException : Exception
    {
        public ATSCommonException() : base() { }
        public ATSCommonException(string message) : base(message) { }
        public ATSCommonException(string message, Exception innerException) : base(message, innerException) { }
    }
}
