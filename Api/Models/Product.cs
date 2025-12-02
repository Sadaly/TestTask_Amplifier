using System.ComponentModel.DataAnnotations;
using Api.Models;
using Api.Models.Abstraction;
using Microsoft.EntityFrameworkCore;

namespace Api.Models
{
    [Index(nameof(Title), IsUnique = true)]
    public class Product : DataEntity
    {
        [Required(ErrorMessage = "Наименование товара обязательно")]
        [StringLength(100, ErrorMessage = "Наименование не может быть длиннее 100 символов")]
        [Display(Name = "Наименование товара")]
        public string Title { get; set; }

        public ICollection<ProductArrival> Arrivals { get; set; } = new List<ProductArrival>();
        public ICollection<ProductExpense> Expenses { get; set; } = new List<ProductExpense>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Product(string title) : base()
        {
            Title = title;
        }
        public Product() : base() { }
    }
}