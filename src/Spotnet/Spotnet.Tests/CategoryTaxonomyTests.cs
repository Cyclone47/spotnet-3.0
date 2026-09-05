using System;
using Spotnet.Model;
using Spotnet.Properties;
using Xunit;

namespace Spotnet.Tests
{
    public class CategoryTaxonomyTests
    {
        [Fact]
        public void CategoriesResources_ContainsGenreStrings()
        {
            // Verify categories resource strings are populated
            Assert.False(string.IsNullOrEmpty(Categories.BGBiography));
            Assert.False(string.IsNullOrEmpty(Categories.BGComicStrip));
            Assert.False(string.IsNullOrEmpty(Categories.BGComputer));
        }

        [Fact]
        public void SpotCat_CanAddChildren()
        {
            var cat = new SpotCat
            {
                Name = "Beeld",
                Tag = "cat0"
            };

            cat.AddChild("DivX");
            cat.AddChild("HD");

            Assert.Equal(2, cat.Children.Count);
            Assert.Equal("Beeld", cat.Name);
            Assert.Equal("cat0", cat.Tag);
        }
    }
}
