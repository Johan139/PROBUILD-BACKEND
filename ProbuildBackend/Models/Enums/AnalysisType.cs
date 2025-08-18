namespace ProbuildBackend.Models.Enums
{
    /// <summary>
    /// Specifies the type of analysis to be performed, ensuring the correct
    /// persona and logic are applied by the AnalysisService.
    /// </summary>
    public enum AnalysisType
    {
        /// <summary>
        /// A targeted analysis using one or more sub-prompts, governed by the sub-contractor master persona.
        /// </summary>
        Selected,

        /// <summary>
        /// A specialized analysis for renovation projects, using the dedicated renovation persona.
        /// </summary>
        Renovation
    }
}