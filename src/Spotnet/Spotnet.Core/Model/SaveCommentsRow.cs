namespace Spotnet.Model;

internal class SaveCommentsRow
{
	public int CommentsAdded;

	public int CommentsDeleted;

	public SaveCommentsRow()
	{
		CommentsAdded = 0;
		CommentsDeleted = 0;
	}

	public void Add(SaveCommentsRow rowToAdd)
	{
		if (rowToAdd != null)
		{
			CommentsAdded += rowToAdd.CommentsAdded;
			CommentsDeleted += rowToAdd.CommentsDeleted;
		}
	}
}
