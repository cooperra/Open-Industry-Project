using Godot;
using System;

[Tool]
public partial class CurvedConveyorAssembly : ConveyorAssembly
{
	protected override float GetPositionOnLegStandsPath(Vector3 position) {
		return (float) Math.Round(Mathf.RadToDeg(new Vector3(0, 0, 1).SignedAngleTo(position.Slide(Vector3.Up), Vector3.Up)));
	}

	protected override void MoveLegStandToPathPosition(Node3D legStand, float position) {
		float radius = this.Scale.X * 1.5f;
		float angle = Mathf.DegToRad(position);
		legStand.Position = new Vector3(0, legStand.Position.Y, radius).Rotated(Vector3.Up, angle);
		legStand.Rotation = new Vector3(0f, angle, legStand.Rotation.Z);
	}

	protected override (float, float) GetLegStandCoverage() {
		// TODO account for rotation between legStands and conveyors
		return (-90f, 0f);
	}
}
