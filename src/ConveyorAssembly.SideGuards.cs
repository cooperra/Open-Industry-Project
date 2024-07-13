using Godot;

public partial class ConveyorAssembly : Node3D
{
	#region SideGuards
	#region SideGuards / Update "LeftSide" and "RightSide" nodes
	private void UpdateSides()
	{
		UpdateSide(rightSide, true);
		UpdateSide(leftSide, false);
	}

	private void UpdateSide(Node3D side, bool isRight) {
		if (side == null || conveyors == null) {
			return;
		}
		LockSidePosition(side, isRight);
		var conveyorLineLength = GetConveyorLineLength();
		ScaleSideGuardLine(side, conveyorLineLength);
		// This would be a great place to implement proportional positioning of other Node3Ds attached to sides when the assembly scales.
	}

	private void LockSidePosition(Node3D side, bool isRight) {
		// Sides always snap onto the conveyor line
		side.Transform = conveyors.Transform;
		var offsetZ = (isRight? -1 : 1) * side.Basis.Z * (this.Scale.Z - 1f);
		side.Position += offsetZ;
	}
	#endregion SideGuards / Update "LeftSide" and "RightSide" nodes

	#region SideGuards / ScaleSideGuardLine
	/**
	 * Scale all side guard children of a given node.
	 *
	 * This would be a great place to implement proportional scaling and positioning of the guards,
	 * but currently, we just scale every guard to the length of the whole line and leave its position alone.
	 *
	 * @param guardLine The parent of the side guards.
	 * @param conveyorLineLength The length of the conveyor line to scale to. Ignored if AutoScaleGuards is false.
	 */
	private void ScaleSideGuardLine(Node3D guardLine, float conveyorLineLength) {
		foreach (Node child in guardLine.GetChildren()) {
			Node3D child3d = child as Node3D;
			if (IsSideGuard(child3d)) {
				SetEditableInstance(child3d, true);
				ScaleSideGuard(child3d, conveyorLineLength);
			}
		}
	}

	private bool IsSideGuard(Node node) {
		return node as SideGuard != null || node as SideGuardCBC != null;
	}

	protected virtual void ScaleSideGuard(Node3D guard, float guardLength) {
		if (AutoScaleGuards) {
			guard.Scale = new Vector3(guardLength, 1f, 1f);
		}
	}
	#endregion SideGuards / ScaleSideGuardLine
	#endregion SideGuards
}
