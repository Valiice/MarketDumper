using System.Collections.Generic;
using System.Threading.Tasks;
using MarketDumper.Models;

namespace MarketDumper.Services;

public interface IRetainerListingReader
{
    Task<List<RetainerListing>> ReadListingsAsync();
}
