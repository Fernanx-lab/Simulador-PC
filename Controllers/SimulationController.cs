using Microsoft.AspNetCore.Mvc;
using ProjetoSimuladorPC.Utilidades;

namespace ProjetoSimuladorPC.Controllers
{
    [ApiController]
    [Route("api/simulation")]
    public class SimulationController : ControllerBase
    {
        private readonly SimulationState _simulation;

        public SimulationController(SimulationState simulation)
        {
            _simulation = simulation;
        }

        /// <summary>
        /// Retorna um snapshot do estado da simulação.
        /// </summary>
        [HttpGet("snapshot")]
        public ActionResult<SimulationSnapshot> GetSnapshot([FromQuery] int ramPreviewAddress = 0, [FromQuery] int ramPreviewLength = 16)
        {
            var snap = _simulation.GetSnapshot(ramPreviewAddress, ramPreviewLength);
            return Ok(snap);
        }

        /// <summary>
        /// Avança o clock da simulação. Delta (opcional) indica quantos ciclos avançar (default 1).
        /// </summary>
        [HttpPost("advance")]
        public IActionResult Advance([FromQuery] int delta = 1)
        {
            _simulation.AdvanceCycle(delta);
            return Ok();
        }
    }
}