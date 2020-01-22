package xamarin.android;

import org.apache.tools.ant.Project;
import org.apache.tools.ant.taskdefs.Java;
import org.apache.tools.ant.types.Commandline.Argument;
import org.apache.tools.ant.types.Path;
import org.w3c.dom.*;
import org.xml.sax.InputSource;

import javax.xml.parsers.*;
import javax.xml.transform.*;
import javax.xml.transform.dom.DOMSource;
import javax.xml.transform.stream.StreamResult;
import java.io.*;
import java.util.*;

/**
 * Class that provides a Java "daemon" for running multiple jar files / Main classes within the same long-running process.
 *
 * The daemon will read one line of stdin and parse it into XML. It will reply with one line of XML.
 *
 * Examples of input:
 * <Java ClassName="com.android.tools.r8.D8" Jar="C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Xamarin\Android\r8.jar" Arguments="--version" />
 * <Java ClassName="com.android.tools.r8.D8" Jar="C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Xamarin\Android\r8.jar" Arguments="--version" />
 * <Java ClassName="com.android.tools.r8.R8" Jar="C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Xamarin\Android\r8.jar" Arguments="--version" />
 * <Java ClassName="com.android.tools.r8.R8" Jar="C:\Program Files (x86)\Microsoft Visual Studio\2019\Preview\MSBuild\Xamarin\Android\r8.jar" Arguments="--version" />
 * <Java Exit="True" />
 *
 * Examples of output:
 * <Java ExitCode="0" StandardOutput="D8 1.5.68&#13;&#10;build engineering&#13;&#10;"/>
 * <Java ExitCode="-1" StandardError="org.xml.sax.SAXParseException; lineNumber: 1; columnNumber: 1; Premature end of file.&#13;&#10;&#9;at com.sun.org.apache.xerces.internal.parsers.DOMParser.parse(DOMParser.java:257)&#13;&#10;&#9;at com.sun.org.apache.xerces.internal.jaxp.DocumentBuilderImpl.parse(DocumentBuilderImpl.java:339)&#13;&#10;&#9;at xamarin.android.JavaDaemon.run(JavaDaemon.java:79)&#13;&#10;&#9;at xamarin.android.JavaDaemon.main(JavaDaemon.java:45)&#13;&#10;"/>
 */
public class JavaDaemon {
    private DocumentBuilder builder;
    private Transformer transformer;

    /**
     * Main entry point
     * @param args
     */
    public static void main (String[] args) {
        try {
            for (String arg : args) {
                if (arg.contentEquals("-help") || arg.contentEquals("--help")) {
                    printHelp();
                    return;
                }
            }
            JavaDaemon daemon = new JavaDaemon();
            daemon.run();
        } catch (Exception e) {
            e.printStackTrace();
        }
    }

    static void printHelp() {
        System.out.println("This tool provides a Java \"daemon\" for running multiple jar files / Main classes within the same long-running process.");
        System.out.println("The daemon will read one line of stdin and parse it into XML. It will reply with one line of XML.");
        System.out.println();
        System.out.println("Example of input:");
        System.out.println("<Java ClassName=\"com.android.tools.r8.D8\" Jar=\"path\\to\\r8.jar\" Arguments=\"--version\" />");
        System.out.println();
        System.out.println("Example of output:");
        System.out.println("<Java ExitCode=\"0\" StandardOutput=\"D8 1.5.68&#13;&#10;build engineering&#13;&#10;\"/>");
    }

    /**
     * Class ctor that configures XML parsers, etc.
     * @throws ParserConfigurationException
     * @throws TransformerConfigurationException
     */
    public JavaDaemon()
            throws ParserConfigurationException, TransformerConfigurationException {
        DocumentBuilderFactory factory = DocumentBuilderFactory.newInstance();
        builder = factory.newDocumentBuilder();
        TransformerFactory transformerFactory = TransformerFactory.newInstance();
        transformer = transformerFactory.newTransformer();
        transformer.setOutputProperty(OutputKeys.OMIT_XML_DECLARATION, "yes");
    }

    /**
     * Starts a loop that reads XML from stdin and writes XML to stdout
     * @throws IOException
     * @throws TransformerException
     */
    public void run()
            throws IOException, TransformerException {
        Scanner scanner = new Scanner(System.in);
        while (true) {
            StringReader reader = null;
            try {
                //This line throws NoSuchElementException if the parent process dies
                String line = scanner.nextLine();
                reader = new StringReader(line);
                Document document = builder.parse(new InputSource(reader));
                Element input = document.getDocumentElement();
                if (!input.getAttribute("Exit").isEmpty()) {
                    break;
                }
                exec(input);
            } catch (NoSuchElementException e) {
                //This means that scanner.nextLine() reached the end, we can exit
                break;
            } catch (Exception e) {
                out (-1, "", toErrorString (e));
            } finally {
                if (reader != null)
                    reader.close();
            }
            // Try to free as much memory as we can while idle
            System.gc();
        }
        scanner.close();
    }

    /**
     * Runs a Java "process" in-process. Using the XML Element as input.
     * @param input
     * @throws IOException
     * @throws TransformerException
     */
    void exec (Element input)
            throws IOException, TransformerException {
        PrintStream oldSystemOut = System.out;
        PrintStream oldSystemErr = System.err;
        try (ByteArrayOutputStream outStream = new ByteArrayOutputStream();
             ByteArrayOutputStream errStream = new ByteArrayOutputStream();
             PrintStream outPrintStream = new PrintStream(outStream, true);
             PrintStream errPrintStream = new PrintStream(errStream, true)) {
            System.setOut(outPrintStream);
            System.setErr(errPrintStream);
            int exitCode = 0;
            try {
                Java java = new Java();
                java.setProject(new Project());
                java.setClassname(input.getAttribute("ClassName"));
                Argument arg = java.getCommandLine().createArgument();
                arg.setLine(input.getAttribute("Arguments"));

                Path path = java.createClasspath();
                path.setPath(input.getAttribute("Jar"));
                java.setClasspath(path);

                exitCode = java.executeJava();
            } finally {
                System.setOut(oldSystemOut);
                System.setErr(oldSystemErr);
            }
            // NOTE: we have to call out() *after* System.out/err is restored
            out(exitCode, outStream.toString(), errStream.toString());
        }
    }

    /**
     * Constructs the reply as an XML document, and prints to stdout.
     * @param exitCode the resulting exit code of the Java process
     * @param out a string containing the contents of stdout
     * @param err a string containing the contents of stderr
     * @throws TransformerException
     */
    void out (int exitCode, String out, String err)
            throws TransformerException {
        Document document = builder.newDocument();
        Element java = document.createElement("Java");
        java.setAttribute("ExitCode", Integer.toString(exitCode));
        if (!out.isEmpty())
            java.setAttribute("StandardOutput", out);
        if (!err.isEmpty())
            java.setAttribute("StandardError", err);
        document.appendChild(java);
        transformer.transform(new DOMSource(document), new StreamResult(System.out));
        System.out.println();
    }

    /**
     * Converts a java Throwable to a useful error string.
     * @param t the Throwable
     * @return a string representation of the error
     * @throws IOException
     */
    static String toErrorString (Throwable t)
            throws IOException {
        try (StringWriter sw = new StringWriter()) {
            try (PrintWriter pw = new PrintWriter(sw)) {
                t.printStackTrace(pw);
            }
            return sw.toString();
        }
    }
}
