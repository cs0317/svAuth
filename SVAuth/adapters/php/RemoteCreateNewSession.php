<?php
    session_start();

	$key = substr(hash('sha256',session_id()),strlen(session_id()));
	$key = utf8_encode($key).substr(256 / 8);
  
  $json_string = file_get_contents("../adapter_config/adapter_config.json");
  $config = json_decode($json_string, true);
  echo "conckey recalculation=" . $key . "<br>";
  $filename = $config["AgentSettings"]["scheme"] . "://" . $config["AgentSettings"]["agentHostname"] . ":"
                            . $config["AgentSettings"]["port"] . "/CheckAuthCode?authcode=" . $_GET["authcode"];

  $respText = file_get_contents($filename);
/*
  ********  for some reason, file_get_contents may not always work. curl is an alternative *******
  $respText = shell_exec("curl -k " . $filename);
*/
  echo "<br>" . $filename;
  echo "<br>respText=" . $respText . ".<br>";
  $entry = json_decode($respText, true);
  $conc = $entry["userProfile"];

  
  if (strcmp($key,$entry["conckey"])!=0 || strcmp($key,$_GET["conckey"])!=0)
    throw new Exception("conckey mismatch!");
  $concdst= $config["WebAppSettings"]["scheme"] . "://" . $config["WebAppSettings"]["hostname"] . ":" . $config["WebAppSettings"]["port"] 
             . "?" . $config["WebAppSettings"]["platform"]["name"];
  echo "<br>concdst=" . concdst;
  if (strcmp($concdst,$entry["concdst"])!=0)
    throw new Exception("concdst mismatch!");
	var_dump($conc);
	echo "LandingUrl" . $_COOKIE["LandingUrl"];
	echo "session id1 is " . session_id() . "<br>";
	session_unset();
  //session_destroy();
  //session_write_close();
	$_SESSION["SVAuth_Email"] = $conc['Email'];
	$_SESSION["SVAuth_UserID"] = $conc['UserID'];
	$_SESSION["SVAuth_FullName"] = $conc['FullName'];
  $_SESSION["SVAuth_Authority"] = $conc['Authority'];
	setcookie(session_name(),'',0,'/');
  session_regenerate_id(true);
	echo "session id2 is " . session_id() . "<br>";
  //session_start();
  echo "session id2 is " . session_id() . "<br>";
  echo ("location:" . $_COOKIE["LandingUrl"] );
	header ("location:" . $_COOKIE["LandingUrl"] );
  
?>
 