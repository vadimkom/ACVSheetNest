namespace DeepNestSharp
{
  using System;
  using System.Collections.Generic;
  using System.Text.Json.Nodes;

  /// <summary>
  /// Ctrl+Z for the job (the user, 2026-11-17): the parts and sheets as they stood before each change. A state
  /// is the project's JSON without the job settings, the saved nest and the report's pictures, so an undo puts the rows back and
  /// leaves Advanced Settings and the layouts on screen alone. <see cref="Note"/> is called after anything
  /// may have changed; it keeps the state before only when the project really differs.
  /// </summary>
  public sealed class ProjectHistory
  {
    public const int Depth = 50;

    private readonly Func<string> capture;
    private readonly LinkedList<string> before = new LinkedList<string>();
    private string current;
    private bool restoring;

    public ProjectHistory(Func<string> capture)
    {
      this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
      this.current = this.capture();
    }

    public bool CanUndo => this.before.Count > 0;

    public int Count => this.before.Count;

    /// <summary>The rows of a project's JSON: no job settings, no nest.</summary>
    public static string Rows(string projectJson)
    {
      if (string.IsNullOrWhiteSpace(projectJson))
      {
        return projectJson;
      }

      var root = JsonNode.Parse(projectJson) as JsonObject;
      if (root == null)
      {
        return projectJson;
      }

      root.Remove("Config");
      root.Remove("LastNestResultJson");
      root.Remove("Pictures"); // megabytes each, fifty deep: the report's pictures are not undone
      return root.ToJsonString();
    }

    /// <summary>Records the state before a change, if the project changed since the last note.</summary>
    public bool Note()
    {
      if (this.restoring)
      {
        return false;
      }

      string now = this.capture();
      if (now == this.current)
      {
        return false;
      }

      this.before.AddLast(this.current);
      if (this.before.Count > Depth)
      {
        this.before.RemoveFirst();
      }

      this.current = now;
      return true;
    }

    /// <summary>Puts back the state before the last change. A change not noted yet is noted first, so it is
    /// the one undone.</summary>
    public bool Undo(Action<string> restore)
    {
      this.Note();
      if (this.before.Count == 0)
      {
        return false;
      }

      string state = this.before.Last.Value;
      this.before.RemoveLast();
      this.restoring = true;
      try
      {
        restore(state);
      }
      finally
      {
        this.restoring = false;
      }

      this.current = this.capture();
      return true;
    }
  }
}
