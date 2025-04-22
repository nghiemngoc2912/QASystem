using System;
using System.Collections.Generic;

namespace QASystem.Models;

public partial class Question
{
    public int QuestionId { get; set; }

    public int UserId { get; set; }

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public string? ImageUrl { get; set; }

    public bool IsDisabled { get; set; } = false;

    public virtual ICollection<Answer> Answers { get; set; } = new List<Answer>();

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();

    public virtual User User { get; set; } = null!;

    public virtual ICollection<Vote> Votes { get; set; } = new List<Vote>();

    public virtual ICollection<Tag> Tags { get; set; } = new List<Tag>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
