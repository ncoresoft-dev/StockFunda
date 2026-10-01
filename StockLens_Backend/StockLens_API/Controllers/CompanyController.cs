using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StockLens_BusinessLayer.DTOs;
using StockLens_BusinessLayer.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StockLens_API.Controllers
{
    [ApiController]
    [Route("api/companies")]
    [Produces("application/json")]
    public class CompanyController : ControllerBase
    {
        private readonly ICompanyService _companyService;

        public CompanyController(ICompanyService companyService)
        {
            _companyService = companyService;
        }

        /// <summary>
        /// Searches for companies by symbol or company name.
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(IEnumerable<CompanyDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SearchCompanies([FromQuery] string query, [FromQuery] int limit = 10)
        {
            var companies = await _companyService.SearchCompaniesAsync(query, limit);
            return Ok(companies);
        }

        /// <summary>
        /// Retrieves Screener-style company overview (About narrative, key points, website, industry, etc.) via Hybrid strategy.
        /// </summary>
        [HttpGet("{symbol}/overview")]
        [ProducesResponseType(typeof(CompanyOverviewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCompanyOverview(
            [FromRoute] string symbol,
            [FromQuery] string? exchange = "NSE",
            [FromQuery] bool forceRefresh = false)
        {
            var overview = await _companyService.GetCompanyOverviewAsync(symbol, exchange, forceRefresh, HttpContext.RequestAborted);
            if (overview == null)
            {
                return NotFound(new { message = $"Company details for symbol '{symbol}' could not be found." });
            }
            return Ok(overview);
        }

        /// <summary>
        /// Alias endpoint for company details
        /// </summary>
        [HttpGet("{symbol}")]
        [ProducesResponseType(typeof(CompanyOverviewDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCompany(
            [FromRoute] string symbol,
            [FromQuery] string? exchange = "NSE",
            [FromQuery] bool forceRefresh = false)
        {
            return await GetCompanyOverview(symbol, exchange, forceRefresh);
        }
    }
}
