using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DelsotParser
{
    public class Article
    {
        public string Name { get; set; }

        public string ImageUrl { get; set; }

        public IDictionary<string, string> Data { get; set; } = new Dictionary<string, string>();

        public string Description { get; set; }

        public string LayoutUrl { get; set; }

        public ICollection<string> DocumentUrls { get; set; } = new LinkedList<string>();
    }
}
