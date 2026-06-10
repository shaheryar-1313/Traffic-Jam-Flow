namespace Game
{
    /// <summary>
    /// Represents an object that can occupy a ConveyorFollowerBoard.
    /// Both Shooter and Vehicle implement this interface so boards can
    /// reset their occupant's parent without knowing the concrete type.
    /// </summary>
    public interface IBoardOccupant
    {
        /// <summary>Detach from the board and restore the original parent transform.</summary>
        void ResetParent();
    }
}
