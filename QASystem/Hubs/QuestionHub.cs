using Microsoft.AspNetCore.SignalR;

namespace QASystem.Hubs
{
    public class QuestionHub : Hub
    {
        public async Task SendAnswerNotification(int questionId, string userName, string content)
        {
            await Clients.Group($"Question_{questionId}").SendAsync("ReceiveAnswer", userName, content);
        }

        public async Task SendVoteUpdate(int questionId, int? answerId, int voteCount)
        {
            await Clients.Group($"Question_{questionId}").SendAsync("ReceiveVoteUpdate", answerId, voteCount);
        }

        public async Task JoinQuestionGroup(int questionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Question_{questionId}");
        }
    }
}