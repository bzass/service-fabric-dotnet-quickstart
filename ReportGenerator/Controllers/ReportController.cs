using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;

namespace ReportGenerator.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportController : ControllerBase
    {
        private readonly IReliableStateManager stateManager;

        public ReportController(IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
        }

        [HttpGet("NumberOfVotes")]
        public async Task<IActionResult> Get()
        {
            var reportDictionary =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<string, int>>(ReportGenerator
                    .ReportsDictionaryName);

            using (var tx = this.stateManager.CreateTransaction())
            {
                var result = await reportDictionary.TryGetValueAsync(tx, ReportGenerator.NumberOfvotesEntryName);
                if (result.HasValue)
                {
                    return Ok(result.Value);
                }

                return NotFound();
            }
        }
    }
}
