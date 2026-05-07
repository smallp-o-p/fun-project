using System;
using Godot;

public partial class GameCamera : AnimatableBody3D
{
  public const float CircleBezierFactor = 0.55228475f;
  public const float QuarterTurnRatio = 0.25f;

  [Export] public float PathRadius { get; set; } = 6.0f;
  [Export] public float RotationDurationSeconds { get; set; } = 0.2f;
  [Export] public float CenterLookAngleDegrees { get; set; } = -30.0f;
  [Export] public float CameraMoveSpeed { get; set; } = 30.0f;
  [Export] public float CameraStep { get; set; } = 0.5f;
  [Export] public float ZoomStep { get; set; } = 4.0f;
  [Export] public float MinFov { get; set; } = 15.0f;
  [Export] public float MaxFov { get; set; } = 100.0f;
  [Export] public float ScrollCameraStep { get; set; } = 0.4f;

  private Path3D cameraPath;
  private PathFollow3D pathFollow;
  private Camera3D camera;
  private Option<Tween> rotationTween;

  enum CameraRotation
  {
    Left,
    Right
  }

  enum MoveDirection
  {
    Forward,
    Back,
    Left,
    Right
  }

  public override void _Ready()
  {
    PathRadius = Mathf.Max(1.0f, PathRadius);
    CameraMoveSpeed = Mathf.Max(1.0f, CameraMoveSpeed);
    CameraStep = Mathf.Max(1.0f, CameraStep);
    ScrollCameraStep = Mathf.Max(0.4f, ScrollCameraStep);

    CollisionShape3D shape = GetNodeOrNull<CollisionShape3D>("RadiusObject") ?? throw new InvalidOperationException("Missing radius object");

    if (shape.Shape is not CylinderShape3D circle)
    {
      throw new InvalidOperationException("RadiusObject isn't a CylinderShape3D");
    }
    circle.Radius = PathRadius;

    cameraPath = GetNodeOrNull<Path3D>("CameraPath") ?? throw new InvalidOperationException("Missing CameraPath");
    cameraPath.Curve = BuildCircleCurve(PathRadius);

    pathFollow = GetNodeOrNull<PathFollow3D>("CameraPath/PathFollow3D") ?? throw new InvalidOperationException("Missing CameraPath/PathFollow3D");
    pathFollow.Loop = true;
    pathFollow.RotationMode = PathFollow3D.RotationModeEnum.None;
    pathFollow.Progress = GetQuarterTurnDistance(cameraPath.Curve);


    camera = GetNodeOrNull<Camera3D>("CameraPath/PathFollow3D/Camera") ?? throw new InvalidOperationException("Missing CameraPath/PathFollow3D/Camera");
    camera.Position = Vector3.Zero;

    UpdateCameraOrientation();
  }

  public override void _Process(double delta)
  {

    if (Input.IsActionJustPressed("rotate_camera_l"))
    {
      RotateQuarterTurn(cameraPath.Curve, CameraRotation.Left);
    }
    else if (Input.IsActionJustPressed("rotate_camera_r"))
    {
      RotateQuarterTurn(cameraPath.Curve, CameraRotation.Right);
    }
    UpdateCameraOrientation();
  }

  public override void _PhysicsProcess(double delta)
  {
    Vector2 moveDirection = GetRequestedMoveDirection();
    float verticalStep = GetRequestedVerticalStep();
    float cameraZoom = GetZoom();
    MoveCamera(moveDirection, verticalStep, cameraZoom, delta);
  }

  private static Curve3D BuildCircleCurve(float radius)
  {
    var curve = new Curve3D();
    float handleLength = radius * CircleBezierFactor;

    curve.AddPoint(new Vector3(radius, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, -handleLength), new Vector3(0.0f, 0.0f, handleLength));
    curve.AddPoint(new Vector3(0.0f, 0.0f, radius), new Vector3(handleLength, 0.0f, 0.0f), new Vector3(-handleLength, 0.0f, 0.0f));
    curve.AddPoint(new Vector3(-radius, 0.0f, 0.0f), new Vector3(0.0f, 0.0f, handleLength), new Vector3(0.0f, 0.0f, -handleLength));
    curve.AddPoint(new Vector3(0.0f, 0.0f, -radius), new Vector3(-handleLength, 0.0f, 0.0f), new Vector3(handleLength, 0.0f, 0.0f));
    curve.Closed = true;

    return curve;
  }

  private static float GetQuarterTurnDistance(Curve3D curve)
  {
    ArgumentNullException.ThrowIfNull(curve);

    return curve.GetBakedLength() * QuarterTurnRatio;
  }

  private static Vector3 GetHorizontalLookTarget(Vector3 cameraPosition, Vector3 orbitCenterPosition)
  {
    return new Vector3(orbitCenterPosition.X, cameraPosition.Y, orbitCenterPosition.Z);
  }

  private static Vector3 GetRelativeMovementOffset(Basis cameraBasis, Vector2 inputDirection, float step)
  {
    if (step < 0.0f)
    {
      throw new ArgumentOutOfRangeException(nameof(step), "Step must be zero or greater.");
    }

    Vector3 right = cameraBasis.X;
    right.Y = 0.0f;

    Vector3 forward = -cameraBasis.Z;
    forward.Y = 0.0f;

    Vector2 normalizedInput = inputDirection.Normalized();
    return ((right.Normalized() * normalizedInput.X) + (forward.Normalized() * normalizedInput.Y)) * step;
  }


  private void RotateQuarterTurn(Curve3D curve, CameraRotation rotate)
  {
    float direction = rotate switch
    {
      CameraRotation.Left => 1.0f,
      CameraRotation.Right => -1.0f,
      _ => throw new NotImplementedException(),
    };

    float targetProgress = pathFollow.Progress + (direction * GetQuarterTurnDistance(curve));

    if (rotationTween.Match(IsInstanceValid, () => false))
      rotationTween.IfSome(tween => tween.Kill());

    Tween createdTween = CreateTween()
      .SetTrans(Tween.TransitionType.Sine)
      .SetEase(Tween.EaseType.InOut);
    createdTween.TweenProperty(pathFollow, "progress", targetProgress, RotationDurationSeconds);

    rotationTween = Some(createdTween);
  }

  private static Vector2 GetRequestedMoveDirection()
  {
    Vector2 vec = new(0.0f, 0.0f);
    if (Input.IsActionPressed("camera_forward"))
    {
      vec.Y += 1.0f;
    }
    if (Input.IsActionPressed("camera_back"))
    {
      vec.Y += -1.0f;
    }
    if (Input.IsActionPressed("camera_left"))
    {
      vec.X += -1.0f;
    }
    if (Input.IsActionPressed("camera_right"))
    {
      vec.X += 1.0f;
    }

    return vec;
  }

  private float GetRequestedVerticalStep()
  {
    float verticalStep = 0.0f;
    if (Input.IsActionPressed("camera_up"))
    {
      verticalStep += ScrollCameraStep;
    }
    if (Input.IsActionPressed("camera_down"))
    {
      verticalStep -= ScrollCameraStep;
    }

    return verticalStep;
  }

  private float GetZoom()
  {
    float zoom = 0.0f;
    if (Input.IsActionJustReleased("camera_zoom_in"))
    {
      zoom += ZoomStep;
    }
    if (Input.IsActionJustReleased("camera_zoom_out"))
    {
      zoom -= ZoomStep;
    }

    return zoom;
  }

  private void MoveCamera(Vector2 direction, float verticalStep, float zoom, double delta)
  {
    float movementDistance = CameraMoveSpeed * (float)delta;
    Vector3 movementOffset = GetRelativeMovementOffset(camera.GlobalTransform.Basis, direction, movementDistance) + (Vector3.Up * verticalStep);

    GlobalPosition += movementOffset;
    camera.Fov = Math.Min(Math.Max(camera.Fov - zoom, MinFov), MaxFov);
  }

  private void UpdateCameraOrientation()
  {
    Transform3D cameraTransform = camera.GetGlobalTransformInterpolated();
    Vector3 orbitCenterPosition = cameraPath.GetGlobalTransformInterpolated().Origin;
    Vector3 horizontalLookTarget = GetHorizontalLookTarget(cameraTransform.Origin, orbitCenterPosition);

    if (cameraTransform.Origin.IsEqualApprox(horizontalLookTarget))
      return;

    camera.LookAt(horizontalLookTarget, Vector3.Up);
    camera.RotateObjectLocal(Vector3.Right, Mathf.DegToRad(CenterLookAngleDegrees));
  }
}
