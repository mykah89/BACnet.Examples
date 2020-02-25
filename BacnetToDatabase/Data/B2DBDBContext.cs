using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace BacnetToDatabase.Data
{
    public class B2DBDBContext : DbContext
    {
        public B2DBDBContext(DbContextOptions options) : base(options) { }
    }
}
