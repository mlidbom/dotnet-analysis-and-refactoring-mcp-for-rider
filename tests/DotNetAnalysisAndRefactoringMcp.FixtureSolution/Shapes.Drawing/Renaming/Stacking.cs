namespace Shapes.Drawing.Renaming;

///<summary>A stack of shapes of <typeparamref name="TShape"/>, its layers <typeparamref name="TLayer"/>:<br/>
/// the bottom one first.</summary>
public abstract class Stacking<TLayer, TShape>
   where TLayer : Stacking<TLayer, TShape>
   where TShape : class, IShape
{
   ///<summary>Puts <paramref name="shape"/>, a <typeparamref name="TShape"/>, on top of this <typeparamref name="TLayer"/>.</summary>
   public abstract TLayer Put(TShape shape);
}
