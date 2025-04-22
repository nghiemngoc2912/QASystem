// Models/QuestionStatsViewModel.cs
namespace QASystem.Models
{
    public class QuestionStatsViewModel
    {
        public int VoteCount { get; set; } = 0;
        public int AnswerCount { get; set; } = 0;
        public DateTime? LatestAnswerTime { get; set; }
    }
}