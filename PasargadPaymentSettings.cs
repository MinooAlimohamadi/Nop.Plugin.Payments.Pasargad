using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.Pasargad
{
    public class PasargadPaymentSettings : ISettings
    {
        public string PrivateKey { get; set; }
        public string TerminalCode { get; set; }
        public string MerchantCode { get; set; }

    }
}
