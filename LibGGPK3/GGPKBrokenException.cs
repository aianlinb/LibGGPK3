using System;

namespace LibGGPK3;
public class GGPKBrokenException : Exception {
	public GGPK? Ggpk { get; }
	public GGPKBrokenException() : base() { }
	public GGPKBrokenException(GGPK ggpk) : base() {
		Ggpk = ggpk;
	}
	public GGPKBrokenException(string message) : base(message) { }
	public GGPKBrokenException(GGPK ggpk, string message) : base(message) {
		Ggpk = ggpk;
	}
	public GGPKBrokenException(string message, Exception innerException) : base(message, innerException) { }
	public GGPKBrokenException(GGPK ggpk, string message, Exception innerException) : base(message, innerException) {
		Ggpk = ggpk;
	}
}