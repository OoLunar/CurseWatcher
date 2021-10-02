namespace CurseWatcher
{
    /// <summary>
    /// Holds a project id and the latest file id. Based off of a project from CurseForge.
    /// </summary>
    public class Project
    {
        /// <summary>
        /// The project id.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The default file id, presumed to be the latest file id.
        /// </summary>
        public int DefaultFileId { get; set; }
    }
}