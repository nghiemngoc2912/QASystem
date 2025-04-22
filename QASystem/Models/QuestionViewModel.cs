// Models/QuestionViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace QASystem.Models
{
    public class QuestionViewModel
    {
        [Required(ErrorMessage = "Title is required.")]
        [StringLength(255)]
        public string Title { get; set; }

        [Required(ErrorMessage = "Content is required.")]
        public string Content { get; set; }
    }
}