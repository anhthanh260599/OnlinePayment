namespace Appota.Models
{
    public class UsersPay
    {
        public int Id { get; set; }
        public string OrderId { get; set; }
        public string Name { get; set; }
        public string PaymentType { get; set; }

        public int ResultCode { get; set; }
        public string PaymentStatus { get; set; }
        public long Amount { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
