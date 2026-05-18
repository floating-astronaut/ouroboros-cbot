using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    /// <summary>
    /// Each strategy ports one Python model. The Robot owns a (cell × strategy)
    /// instance per whitelist row. Strategy is stateless beyond cached indicators.
    /// </summary>
    public interface IStrategy
    {
        string Id { get; }                  // matches Cell.StrategyId
        void Attach(Robot robot, Bars bars, Cell cell);
        Signal OnBar(Robot robot, Bars bars, Cell cell);  // called on each closed bar of cell.TimeFrame
    }
}
