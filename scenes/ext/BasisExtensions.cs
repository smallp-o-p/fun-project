using Godot;

namespace FunProject.Scenes.Ext;

public static class BasisExtensions
{
  extension(Basis basis)
  {
    public bool IsUpright()
    {
      for (int turn = 0; turn < 4; turn++)
        if (basis.IsEqualApprox(new Basis(Vector3.Up, turn * Mathf.Pi / 2))) return true;
      return false;
    }
  }

}