namespace QASystem.Models
{
    public class Material
    {
        public int MaterialId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string FileLink { get; set; }
        public int Downloads { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? UserId { get; set; }
        public User User { get; set; }
    }
}