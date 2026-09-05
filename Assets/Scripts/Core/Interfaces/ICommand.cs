namespace MobileDemo.Core.Interfaces
{
    // Two methods, and there is deliberately no CanUndo and no bool return. Undoing a sell has to
    // charge its refund back, and that refund can have been spent in the meantime -- which reads
    // like a case for a failure path. It is not: BuildController is the only spender and pops
    // strictly LIFO, so any spend made after a sell sits *above* it on the stack and has already
    // been undone and refunded by the time the sell's Undo runs. The stack discipline is what
    // makes this interface sufficient, not the arithmetic. See ARCHITECTURE.md §6.
    public interface ICommand
    {
        void Execute();

        void Undo();
    }
}
