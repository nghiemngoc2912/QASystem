using QASystem.Models;

namespace QASystem.ViewModels
{
    public class MaterialListViewModel
    {
        public List<Material> Materials { get; set; } = new List<Material>();
        public string SearchTerm { get; set; } = string.Empty;
        public string SortOrder { get; set; } = "desc";
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalItems { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
    }
}
