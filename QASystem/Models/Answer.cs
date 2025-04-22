using System;
using System.Collections.Generic;

namespace QASystem.Models;

public partial class Answer
{
    public int AnswerId { get; set; }

    public int QuestionId { get; set; }

    public int? UserId { get; set; }

    public int? ParentAnswerId { get; set; }

    public string Content { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsDisabled { get; set; } = false;
    public virtual Question Question { get; set; } = null!;

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();

    public virtual User? User { get; set; }

    public virtual ICollection<Vote> Votes { get; set; } = new List<Vote>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual Answer? ParentAnswer { get; set; }

    public virtual ICollection<Answer> ChildAnswers { get; set; } = new List<Answer>();
}
