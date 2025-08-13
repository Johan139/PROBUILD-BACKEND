using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class ConversationPromptKey
{
    [Key]
    public int ConversationPromptID { get; set; }

    [Required]
    public string ConversationId { get; set; }

    [ForeignKey("ConversationId")]
    public virtual Conversation Conversation { get; set; }

    [Required]
    [StringLength(255)]
    public string PromptKey { get; set; }
}