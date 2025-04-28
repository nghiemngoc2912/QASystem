using System.ComponentModel.DataAnnotations;

namespace QASystem.Models
{
    public class Material
    {
        public int MaterialId { get; set; }
        [Required(ErrorMessage = "Title is required.")]
        [StringLength(100, ErrorMessage = "Title cannot be longer than 100 characters.")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Description is required.")]
        [StringLength(500, ErrorMessage = "Description cannot be longer than 500 characters.")]
        public string Description { get; set; }
        public string FileLink { get; set; }
        public int Downloads { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? UserId { get; set; }
        public User? User { get; set; }
    }
}