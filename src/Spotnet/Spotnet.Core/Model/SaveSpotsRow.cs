namespace Spotnet.Model;

public class SaveSpotsRow
{
	public int[] NewCats;

	public int SpotsAdded;

	public int SpotsDeleted;

	public SaveSpotsRow()
	{
		NewCats = new int[11];
		SpotsAdded = 0;
		SpotsDeleted = 0;
	}

	public void Add(SaveSpotsRow rowToAdd)
	{
		if (rowToAdd != null)
		{
			SpotsAdded += rowToAdd.SpotsAdded;
			SpotsDeleted += rowToAdd.SpotsDeleted;
			for (int i = 0; i < NewCats.Length; i++)
			{
				NewCats[i] += rowToAdd.NewCats[i];
			}
		}
	}

	public SaveSpotsRow Copy()
	{
		SaveSpotsRow saveSpotsRow = new SaveSpotsRow
		{
			SpotsAdded = SpotsAdded,
			SpotsDeleted = SpotsDeleted
		};
		for (int i = 0; i < NewCats.Length; i++)
		{
			saveSpotsRow.NewCats[i] += NewCats[i];
		}
		return saveSpotsRow;
	}

	public static SaveSpotsRow operator +(SaveSpotsRow o1, SaveSpotsRow o2)
	{
		SaveSpotsRow saveSpotsRow = new SaveSpotsRow();
		saveSpotsRow.Add(o1);
		saveSpotsRow.Add(o2);
		return saveSpotsRow;
	}
}
