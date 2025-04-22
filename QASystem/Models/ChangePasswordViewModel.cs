using System.ComponentModel.DataAnnotations;

namespace QASystem.Models
{
	public class ChangePasswordViewModel
	{
		[Required(ErrorMessage = "Old password is required.")]
		[DataType(DataType.Password)]
		public string OldPassword { get; set; }

		[Required(ErrorMessage = "New password is required.")]
		[StringLength(100, ErrorMessage = "The {0} must be at least {2} and at most {1} characters long.", MinimumLength = 6)]
		[DataType(DataType.Password)]
		public string NewPassword { get; set; }

		[DataType(DataType.Password)]
		[Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
		public string ConfirmPassword { get; set; }
	}
}