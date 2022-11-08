/*
 * monodroid-networkinfo.c
 *
 * Authors:
 *      Marek Habersack <grendel@twistedcode.net>
 *
 * Copyright (C) 2016 Microsoft (http://microsoft.com)
 *
 * Permission is hereby granted, free of charge, to any person obtaining
 * a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including
 * without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to
 * permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
 * LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
 * OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */
#include <pthread.h>
#include <cstring>

#include "monodroid.h"
#include "monodroid-glue.hh"
#include "strings.hh"

#include "android-system.hh"
#include "util.hh"

using namespace xamarin::android;
using namespace xamarin::android::internal;

static pthread_once_t java_classes_once_control = PTHREAD_ONCE_INIT;
static jclass NetworkInterface_class;
static jmethodID NetworkInterface_getByName;
static jmethodID NetworkInterface_isUp;
static jmethodID NetworkInterface_supportsMulticast;

static void
java_classes_init ()
{
	JNIEnv *env = get_jnienv ();
	NetworkInterface_class = env->FindClass ("java/net/NetworkInterface");
	NetworkInterface_class = reinterpret_cast <jclass> (env->NewGlobalRef (NetworkInterface_class));
	NetworkInterface_getByName = env->GetStaticMethodID (NetworkInterface_class, "getByName", "(Ljava/lang/String;)Ljava/net/NetworkInterface;");
	NetworkInterface_isUp = env->GetMethodID (NetworkInterface_class, "isUp", "()Z");
	NetworkInterface_supportsMulticast = env->GetMethodID (NetworkInterface_class, "supportsMulticast", "()Z");
}

static mono_bool
_monodroid_get_network_interface_state (const char *ifname, mono_bool *is_up, mono_bool *supports_multicast)
{
	if (!ifname || strlen (ifname) == 0 || (!is_up && !supports_multicast))
		return FALSE;

	mono_bool ret = TRUE;

	if (is_up)
		*is_up = FALSE;
	if (supports_multicast)
		*supports_multicast = FALSE;

	pthread_once (&java_classes_once_control, java_classes_init);

	JNIEnv *env = nullptr;
	jstring NetworkInterface_nameArg = nullptr;
	jobject networkInterface = nullptr;

	if (!NetworkInterface_class || !NetworkInterface_getByName) {
		if (!NetworkInterface_class)
			log_warn (LOG_NET, "Failed to find the 'java.net.NetworkInterface' Java class");
		if (!NetworkInterface_getByName)
			log_warn (LOG_NET, "Failed to find the 'java.net.NetworkInterface.getByName' function");
		log_warn (LOG_NET, "Unable to determine network interface state because of missing Java API");
		goto leave;
	}

	env = get_jnienv ();
	NetworkInterface_nameArg = env->NewStringUTF (ifname);
	networkInterface = env->CallStaticObjectMethod (NetworkInterface_class, NetworkInterface_getByName, NetworkInterface_nameArg);
	env->DeleteLocalRef (NetworkInterface_nameArg);
	if (env->ExceptionOccurred ()) {
		log_warn (LOG_NET, "Java exception occurred while looking up the interface '%s'", ifname);
		env->ExceptionDescribe ();
		env->ExceptionClear ();
		goto leave;
	}

	if (!networkInterface) {
		log_warn (LOG_NET, "Failed to look up interface '%s' using Java API", ifname);
		ret = FALSE;
		goto leave;
	}

	if (is_up) {
		if (!NetworkInterface_isUp) {
			log_warn (LOG_NET, "Failed to find the 'java.net.NetworkInterface.isUp' function. Unable to determine interface operational state");
			ret = FALSE;
		} else
			*is_up = (mono_bool)env->CallBooleanMethod (networkInterface, NetworkInterface_isUp);
	}

	if (supports_multicast) {
		if (!NetworkInterface_supportsMulticast) {
			log_warn (LOG_NET, "Failed to find the 'java.net.NetworkInterface.supportsMulticast' function. Unable to determine whether interface supports multicast");
			ret = FALSE;
		} else
			*supports_multicast = (mono_bool)env->CallBooleanMethod (networkInterface, NetworkInterface_supportsMulticast);
	}

  leave:
	if (!ret)
		log_warn (LOG_NET, "Unable to determine interface '%s' state using Java API", ifname);

	if (networkInterface != nullptr && env != nullptr) {
		env->DeleteLocalRef (networkInterface);
	}

	return ret;
}

mono_bool
_monodroid_get_network_interface_up_state (const char *ifname, mono_bool *is_up)
{
	return _monodroid_get_network_interface_state (ifname, is_up, nullptr);
}

mono_bool
_monodroid_get_network_interface_supports_multicast (const char *ifname, mono_bool *supports_multicast)
{
	return _monodroid_get_network_interface_state (ifname, nullptr, supports_multicast);
}

int
_monodroid_get_dns_servers (void **dns_servers_array)
{
	if (!dns_servers_array) {
		log_warn (LOG_NET, "Unable to get DNS servers, no location to store data in");
		return -1;
	}
	*dns_servers_array = nullptr;

	size_t  len;
	dynamic_local_string<PROPERTY_VALUE_BUFFER_LEN> dns;
	char   *dns_servers [8];
	int     count = 0;
	char    prop_name[] = "net.dnsX";
	for (int i = 0; i < 8; i++) {
		prop_name [7] = (char)(i + 0x31);
		len = static_cast<size_t>(AndroidSystem::monodroid_get_system_property (prop_name, dns));
		if (len == 0) {
			dns_servers [i] = nullptr;
			continue;
		}
		dns_servers [i] = strndup (dns.get (), len);
		count++;
	}

	if (count <= 0)
		return 0;

	auto alloc_size = MULTIPLY_WITH_OVERFLOW_CHECK (size_t, sizeof (char*), static_cast<size_t>(count));
	char **ret = (char**)malloc (alloc_size);
	char **p = ret;
	for (int i = 0; i < 8; i++) {
		if (!dns_servers [i])
			continue;
		*p++ = dns_servers [i];
	}

	*dns_servers_array = (void*)ret;
	return count;
}
