

using Microsoft.AspNetCore.Identity;

namespace QASystem.Models;

public partial class User : IdentityUser<int>
{
    public int Reputation { get; set; }

    public DateTime CreatedAt { get; set; }

    public string AvatarUrl { get; set; } = "/images/avatars/default.png";

    public virtual ICollection<Answer> Answers { get; set; } = new List<Answer>();

    public virtual ICollection<Question> Questions { get; set; } = new List<Question>();

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();

    public virtual ICollection<Vote> Votes { get; set; } = new List<Vote>();

    public virtual ICollection<Material> Materials { get; set; } = new List<Material>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
