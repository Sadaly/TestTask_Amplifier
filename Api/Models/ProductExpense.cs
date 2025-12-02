using System.ComponentModel.DataAnnotations;
using Api.Models.Abstraction;

namespace Api.Models
{
    public class ProductExpense : DataEntity
    {
        [Required]
        public Guid ProductId { get; set; }

        [Required, Range(1, int.MaxValue)]
        public int Amount { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public Product Product { get; set; }

        public ProductExpense(Guid productId, int amount, DateTime date) : base()
        {
            ProductId = productId;
            Amount = amount;
            Date = date;
        }
        public ProductExpense() : base() { }
    }
}