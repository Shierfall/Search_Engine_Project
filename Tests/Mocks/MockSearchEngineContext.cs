using Microsoft.EntityFrameworkCore;
using SearchEngine.Persistence;
using SearchEngine.Persistence.Entities;

namespace SearchEngine.Tests.Mocks
{
    public class MockSearchEngineContext : SearchEngineContext
    {
        public MockSearchEngineContext() 
            : base(new DbContextOptionsBuilder<SearchEngineContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{System.Guid.NewGuid()}")
                .Options)
        {
        }
    }
} 