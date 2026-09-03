namespace ScreenRecorderApp.Models;

/// <summary>
/// A drawing tool on the live annotation overlay's floating toolbar. <see cref="Pen"/> is freehand;
/// the four shape tools rubber-band between a press and a release; <see cref="Text"/> drops a typed
/// label at the click point.
/// </summary>
public enum AnnotationTool
{
    Pen,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Text,
}
