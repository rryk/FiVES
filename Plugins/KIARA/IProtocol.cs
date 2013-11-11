using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace KIARAPlugin
{
    /// <summary>
    /// Handler for OnClose event.
    /// </summary>
    /// <param name="reason">Reason for closing.</param>
    public delegate void Close(string reason);

    /// <summary>
    /// The interface that should be implemented by a protocol wrapper. Each object implementing this interface
    /// corresponds to a connection between two parties.
    /// </summary>
    public interface IProtocol
    {
        // Triggered when the connection is closed.
        event Close OnClose;

        /// <summary>
        /// Processes the parsed KIARA IDL file.
        /// </summary>
        /// <remarks>
        /// Currently an unparsed string is used as parsing was not implemented yet.
        /// </remarks>
        /// <param name="parsedIDL">Parsed KIARA IDL file.</param>
        void ProcessIDL(string parsedIDL);

        /// <summary>
        /// Calls the function <paramref name="name"/> with <paramref name="args"/> as parameters on the remote end.
        /// </summary>
        /// <returns>The call object representing started call.</returns>
        /// <param name="name">Name of the function to be called.</param>
        /// <param name="args">Arguments to be passed as parameters into function.</param>
        IFuncCall CallFunc(string name, params object[] args);

        // Registers a |handler| for the function |name|, which is called when remote end requests the function to be
        // called. Returned value from |handler| (if any) is returned back to the caller. If called again for the same
        // |name| - throws an exception.

        /// <summary>
        /// Registers the <paramref name="handler"/> to be executed when function with <paramref name="name"/> is called
        /// by the remote end. When called twice for the same <paramref name="name"/>, the <paramref name="handler"/>
        /// replaces previous value. The value returned from the <paramref name="handler"/> is passed back to the caller
        /// as a return value.
        /// </summary>
        /// <param name="name">Name of the function.</param>
        /// <param name="handler">Handler to be executed upon a call.</param>
        void RegisterHandler(string name, Delegate handler);

        /// <summary>
        /// Terminates the connection.
        /// </summary>
        void Disconnect();
    }
}