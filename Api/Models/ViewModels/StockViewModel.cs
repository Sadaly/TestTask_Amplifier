namespace Api.Models.ViewModels
{
    public class StockViewModel
    {
        public Product Product { get; set; }
        public int CurrentStock { get; set; }
        public DateTime? LastArrival { get; set; }
        public DateTime? LastExpense { get; set; }
    }
}
