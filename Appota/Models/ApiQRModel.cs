namespace Appota.Models
{
    public class ApiQRModel
    {
        public string errorCode { get; set; }
        public string message { get; set; }
        public string orderId { get; set; }
        public string amount { get; set; }
        public string paymentUrl { get; set; }
        public string signature { get; set; }
    }
}
