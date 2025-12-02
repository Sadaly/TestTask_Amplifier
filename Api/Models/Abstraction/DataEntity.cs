using Microsoft.EntityFrameworkCore;

namespace Api.Models.Abstraction
{
    [PrimaryKey(propertyName: nameof(Id))]
    public abstract class DataEntity
    {
        public Guid Id { get; set; }
        protected DataEntity()
        {
            Id = Guid.NewGuid();
        }
    }
}
