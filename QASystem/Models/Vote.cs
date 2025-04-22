using System;
using System.Collections.Generic;

namespace QASystem.Models;

public partial class Vote
{
    public int VoteId { get; set; }

    public int UserId { get; set; }

    public int? QuestionId { get; set; }

    public int? AnswerId { get; set; }

    public int? VoteType { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual Answer? Answer { get; set; }

    public virtual Question? Question { get; set; }

    public virtual User User { get; set; } = null!;
}
