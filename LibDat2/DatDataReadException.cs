using System;

namespace LibDat2 {
	public class DatReadException : Exception {
		public DatReadException(string message) : base(message) {
		}
		public DatReadException(string message, Exception innerException) : base(message, innerException) {
		}
	}

	public class DatDataReadException : DatReadException {
		public string DatName;
		public int Row;
		public int Column;
		public string FieldName;
		public long StreamPosition;
		public long LastSucceededPosition;

		public DatDataReadException(string datName, int row, int column, string fieldName, long streamPosition, long lastSucceededPosition, Exception innerException) : base(
@$"Error While Reading: {datName}
At Row:{row},
Column:{column} ({fieldName}),
StreamPosition:{streamPosition},
LastSucceededPosition:{lastSucceededPosition}

{innerException.Message}"
			, innerException) {
			DatName = datName;
			Row = row;
			Column = column;
			FieldName = fieldName;
			StreamPosition = streamPosition;
			LastSucceededPosition = lastSucceededPosition;
		}
	}
}